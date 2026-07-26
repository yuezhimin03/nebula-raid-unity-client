using System;
using System.Collections.Generic;

namespace NebulaRaid.Combat
{
    /// <summary>
    /// Uniform-grid broad phase. Buckets are reused, populated in entity-id order,
    /// and queried in fixed x/y order; dictionary iteration never affects results.
    /// </summary>
    internal sealed class DeterministicSpatialGrid
    {
        private readonly int _cellSizeMm;
        private readonly Dictionary<long, List<int>> _buckets = new Dictionary<long, List<int>>();
        private readonly List<List<int>> _activeBuckets = new List<List<int>>();

        public DeterministicSpatialGrid(int cellSizeMm)
        {
            _cellSizeMm = cellSizeMm;
        }

        public void Rebuild(CombatWorld world)
        {
            for (int i = 0; i < _activeBuckets.Count; i++)
            {
                _activeBuckets[i].Clear();
            }

            _activeBuckets.Clear();
            for (int entityId = 0; entityId < world.Count; entityId++)
            {
                if (!world.Alive[entityId])
                {
                    continue;
                }

                int cellX = FloorDivide(world.PositionX[entityId], _cellSizeMm);
                int cellY = FloorDivide(world.PositionY[entityId], _cellSizeMm);
                long key = MakeKey(cellX, cellY);
                if (!_buckets.TryGetValue(key, out List<int>? bucket))
                {
                    bucket = new List<int>(8);
                    _buckets.Add(key, bucket);
                }

                if (bucket.Count == 0)
                {
                    _activeBuckets.Add(bucket);
                }

                bucket.Add(entityId);
            }
        }

        public int FindNearestEnemy(CombatWorld world, int sourceEntityId, int rangeMm)
        {
            int sourceX = world.PositionX[sourceEntityId];
            int sourceY = world.PositionY[sourceEntityId];
            int sourceCellX = FloorDivide(sourceX, _cellSizeMm);
            int sourceCellY = FloorDivide(sourceY, _cellSizeMm);
            int cellRadius = (rangeMm + _cellSizeMm - 1) / _cellSizeMm;
            long rangeSquared = (long)rangeMm * rangeMm;
            long nearestDistanceSquared = long.MaxValue;
            int nearestEntityId = -1;

            for (int offsetY = -cellRadius; offsetY <= cellRadius; offsetY++)
            {
                for (int offsetX = -cellRadius; offsetX <= cellRadius; offsetX++)
                {
                    long key = MakeKey(sourceCellX + offsetX, sourceCellY + offsetY);
                    if (!_buckets.TryGetValue(key, out List<int>? bucket))
                    {
                        continue;
                    }

                    for (int i = 0; i < bucket.Count; i++)
                    {
                        int candidate = bucket[i];
                        if (candidate == sourceEntityId
                            || !world.Alive[candidate]
                            || world.Team[candidate] == world.Team[sourceEntityId])
                        {
                            continue;
                        }

                        long dx = (long)world.PositionX[candidate] - sourceX;
                        long dy = (long)world.PositionY[candidate] - sourceY;
                        long distanceSquared = (dx * dx) + (dy * dy);
                        if (distanceSquared > rangeSquared)
                        {
                            continue;
                        }

                        if (distanceSquared < nearestDistanceSquared
                            || (distanceSquared == nearestDistanceSquared && candidate < nearestEntityId))
                        {
                            nearestDistanceSquared = distanceSquared;
                            nearestEntityId = candidate;
                        }
                    }
                }
            }

            return nearestEntityId;
        }

        private static int FloorDivide(int value, int divisor)
        {
            int quotient = value / divisor;
            int remainder = value % divisor;
            return remainder < 0 ? quotient - 1 : quotient;
        }

        private static long MakeKey(int x, int y)
        {
            return ((long)x << 32) ^ (uint)y;
        }
    }
}

