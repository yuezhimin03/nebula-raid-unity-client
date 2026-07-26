using System;

namespace NebulaRaid.Resources
{
    public sealed class ResourceLease<T> : IDisposable where T : class
    {
        private ReferenceCountedResourceCache<T>? _owner;
        private readonly object _entry;

        internal ResourceLease(
            ReferenceCountedResourceCache<T> owner,
            object entry,
            string key,
            T value)
        {
            _owner = owner;
            _entry = entry;
            Key = key;
            Value = value;
        }

        public string Key { get; }
        public T Value { get; }

        public void Dispose()
        {
            ReferenceCountedResourceCache<T>? owner = _owner;
            if (owner == null)
            {
                return;
            }

            _owner = null;
            owner.ReleaseOpaque(_entry);
        }
    }
}

