namespace NebulaRaid.Resources
{
    public readonly struct ResourceCacheMetrics
    {
        public ResourceCacheMetrics(
            long hits,
            long misses,
            long evictions,
            int entries,
            long residentWeight,
            long budget)
        {
            Hits = hits;
            Misses = misses;
            Evictions = evictions;
            Entries = entries;
            ResidentWeight = residentWeight;
            Budget = budget;
        }

        public long Hits { get; }
        public long Misses { get; }
        public long Evictions { get; }
        public int Entries { get; }
        public long ResidentWeight { get; }
        public long Budget { get; }
    }
}

