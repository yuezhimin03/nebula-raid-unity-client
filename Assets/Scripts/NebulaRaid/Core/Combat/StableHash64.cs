namespace NebulaRaid.Combat
{
    internal struct StableHash64
    {
        private const ulong OffsetBasis = 14695981039346656037UL;
        private const ulong Prime = 1099511628211UL;
        private ulong _value;

        public static StableHash64 Create()
        {
            return new StableHash64 { _value = OffsetBasis };
        }

        public void Add(bool value)
        {
            Add(value ? 1 : 0);
        }

        public void Add(byte value)
        {
            _value ^= value;
            _value *= Prime;
        }

        public void Add(sbyte value)
        {
            Add(unchecked((byte)value));
        }

        public void Add(int value)
        {
            unchecked
            {
                Add((byte)value);
                Add((byte)(value >> 8));
                Add((byte)(value >> 16));
                Add((byte)(value >> 24));
            }
        }

        public void Add(uint value)
        {
            Add(unchecked((int)value));
        }

        public ulong ToUInt64()
        {
            return _value;
        }
    }
}

