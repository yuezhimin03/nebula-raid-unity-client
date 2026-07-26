using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NebulaRaid.Resources
{
    /// <summary>
    /// Coalesces concurrent loads, pins resources while leased, and evicts only
    /// idle resources by least-recently-used order when the weighted budget is exceeded.
    /// </summary>
    public sealed class ReferenceCountedResourceCache<T> : IDisposable where T : class
    {
        private readonly object _gate = new object();
        private readonly IResourceLoader<T> _loader;
        private readonly Func<T, long> _measure;
        private readonly Action<T>? _onEvicted;
        private readonly long _budget;
        private readonly Dictionary<string, Entry> _entries =
            new Dictionary<string, Entry>(StringComparer.Ordinal);
        private readonly LinkedList<Entry> _idleLru = new LinkedList<Entry>();
        private bool _disposed;
        private long _residentWeight;
        private long _hits;
        private long _misses;
        private long _evictions;

        public ReferenceCountedResourceCache(
            IResourceLoader<T> loader,
            long budget,
            Func<T, long> measure,
            Action<T>? onEvicted = null)
        {
            _loader = loader ?? throw new ArgumentNullException(nameof(loader));
            _measure = measure ?? throw new ArgumentNullException(nameof(measure));
            if (budget < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(budget));
            }

            _budget = budget;
            _onEvicted = onEvicted;
        }

        public async Task<ResourceLease<T>> AcquireAsync(
            string key,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Resource key is required.", nameof(key));
            }

            cancellationToken.ThrowIfCancellationRequested();
            Entry entry;
            lock (_gate)
            {
                ThrowIfDisposed();
                if (_entries.TryGetValue(key, out Entry? existing))
                {
                    entry = existing;
                    entry.ReferenceCount++;
                    if (entry.IdleNode != null)
                    {
                        _idleLru.Remove(entry.IdleNode);
                        entry.IdleNode = null;
                    }

                    _hits++;
                }
                else
                {
                    Task<T> loading = _loader.LoadAsync(key, CancellationToken.None);
                    if (loading == null)
                    {
                        throw new InvalidOperationException("The resource loader returned a null task.");
                    }

                    entry = new Entry(key, loading);
                    _entries.Add(key, entry);
                    _misses++;
                }
            }

            T value;
            try
            {
                value = await entry.Loading.ConfigureAwait(false);
                if (value == null)
                {
                    throw new InvalidOperationException("The resource loader returned null.");
                }
            }
            catch
            {
                RemoveFailedEntry(entry);
                throw;
            }

            lock (_gate)
            {
                if (!_entries.TryGetValue(entry.Key, out Entry? current)
                    || !ReferenceEquals(current, entry))
                {
                    throw new ObjectDisposedException(nameof(ReferenceCountedResourceCache<T>));
                }

                if (!entry.IsReady)
                {
                    long weight = _measure(value);
                    if (weight < 0)
                    {
                        RemoveFailedEntry(entry);
                        throw new InvalidOperationException("Resource weight cannot be negative.");
                    }

                    entry.Value = value;
                    entry.Weight = weight;
                    entry.IsReady = true;
                    _residentWeight = SaturatingAdd(_residentWeight, weight);
                }
            }

            if (cancellationToken.IsCancellationRequested)
            {
                Release(entry);
                cancellationToken.ThrowIfCancellationRequested();
            }

            return new ResourceLease<T>(this, entry, entry.Key, value);
        }

        public ResourceCacheMetrics GetMetrics()
        {
            lock (_gate)
            {
                return new ResourceCacheMetrics(
                    _hits,
                    _misses,
                    _evictions,
                    _entries.Count,
                    _residentWeight,
                    _budget);
            }
        }

        public void Trim()
        {
            lock (_gate)
            {
                ThrowIfDisposed();
                TrimUnderLock();
            }
        }

        public void Dispose()
        {
            List<T> values = new List<T>();
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                foreach (Entry entry in _entries.Values)
                {
                    if (entry.IsReady && entry.Value != null)
                    {
                        values.Add(entry.Value);
                    }
                }

                _entries.Clear();
                _idleLru.Clear();
                _residentWeight = 0;
            }

            for (int i = 0; i < values.Count; i++)
            {
                _onEvicted?.Invoke(values[i]);
            }
        }

        internal void ReleaseOpaque(object opaqueEntry)
        {
            if (!(opaqueEntry is Entry entry))
            {
                throw new ArgumentException("Lease belongs to a different cache.", nameof(opaqueEntry));
            }

            Release(entry);
        }

        private void Release(Entry entry)
        {
            lock (_gate)
            {
                if (!_entries.TryGetValue(entry.Key, out Entry? current)
                    || !ReferenceEquals(current, entry))
                {
                    return;
                }

                if (entry.ReferenceCount <= 0)
                {
                    throw new InvalidOperationException("Resource reference count underflow.");
                }

                entry.ReferenceCount--;
                if (entry.ReferenceCount == 0 && entry.IsReady)
                {
                    entry.IdleNode = _idleLru.AddFirst(entry);
                    TrimUnderLock();
                }
            }
        }

        private void RemoveFailedEntry(Entry entry)
        {
            lock (_gate)
            {
                if (_entries.TryGetValue(entry.Key, out Entry? current)
                    && ReferenceEquals(current, entry))
                {
                    _entries.Remove(entry.Key);
                    if (entry.IsReady)
                    {
                        _residentWeight -= entry.Weight;
                    }
                }
            }
        }

        private void TrimUnderLock()
        {
            while (_residentWeight > _budget && _idleLru.Last != null)
            {
                Entry victim = _idleLru.Last.Value;
                _idleLru.RemoveLast();
                victim.IdleNode = null;
                _entries.Remove(victim.Key);
                _residentWeight -= victim.Weight;
                _evictions++;
                if (victim.Value != null)
                {
                    _onEvicted?.Invoke(victim.Value);
                }
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(ReferenceCountedResourceCache<T>));
            }
        }

        private static long SaturatingAdd(long left, long right)
        {
            return left > long.MaxValue - right ? long.MaxValue : left + right;
        }

        private sealed class Entry
        {
            public Entry(string key, Task<T> loading)
            {
                Key = key;
                Loading = loading;
                ReferenceCount = 1;
            }

            public string Key { get; }
            public Task<T> Loading { get; }
            public T? Value { get; set; }
            public int ReferenceCount { get; set; }
            public long Weight { get; set; }
            public bool IsReady { get; set; }
            public LinkedListNode<Entry>? IdleNode { get; set; }
        }
    }
}

