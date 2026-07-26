using System;
using System.Collections.Generic;

namespace NebulaRaid.Pooling
{
    public readonly struct ObjectPoolMetrics
    {
        public ObjectPoolMetrics(long created, long rented, long returned, long dropped, int retained)
        {
            Created = created;
            Rented = rented;
            Returned = returned;
            Dropped = dropped;
            Retained = retained;
        }

        public long Created { get; }
        public long Rented { get; }
        public long Returned { get; }
        public long Dropped { get; }
        public int Retained { get; }
    }

    /// <summary>A bounded LIFO pool with optional reset and drop hooks.</summary>
    public sealed class ObjectPool<T> where T : class
    {
        private readonly object _gate = new object();
        private readonly Stack<T> _items;
        private readonly Func<T> _factory;
        private readonly Action<T>? _reset;
        private readonly Action<T>? _onDrop;
        private readonly int _maxRetained;
        private long _created;
        private long _rented;
        private long _returned;
        private long _dropped;

        public ObjectPool(
            Func<T> factory,
            Action<T>? reset = null,
            Action<T>? onDrop = null,
            int maxRetained = 128,
            int initialCapacity = 0)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            if (maxRetained < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxRetained));
            }

            if (initialCapacity < 0 || initialCapacity > maxRetained)
            {
                throw new ArgumentOutOfRangeException(nameof(initialCapacity));
            }

            _reset = reset;
            _onDrop = onDrop;
            _maxRetained = maxRetained;
            _items = new Stack<T>(maxRetained);

            for (int i = 0; i < initialCapacity; i++)
            {
                _items.Push(Create());
            }
        }

        public T Rent()
        {
            lock (_gate)
            {
                _rented++;
                return _items.Count > 0 ? _items.Pop() : Create();
            }
        }

        public void Return(T item)
        {
            if (item == null)
            {
                throw new ArgumentNullException(nameof(item));
            }

            lock (_gate)
            {
                _returned++;
                _reset?.Invoke(item);
                if (_items.Count < _maxRetained)
                {
                    _items.Push(item);
                }
                else
                {
                    _dropped++;
                    _onDrop?.Invoke(item);
                }
            }
        }

        public ObjectPoolMetrics GetMetrics()
        {
            lock (_gate)
            {
                return new ObjectPoolMetrics(_created, _rented, _returned, _dropped, _items.Count);
            }
        }

        private T Create()
        {
            T value = _factory();
            if (value == null)
            {
                throw new InvalidOperationException("The pool factory returned null.");
            }

            _created++;
            return value;
        }
    }
}

