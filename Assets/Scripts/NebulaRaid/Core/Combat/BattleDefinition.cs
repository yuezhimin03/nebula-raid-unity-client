using System;
using NebulaRaid.Messaging;

namespace NebulaRaid.Combat
{
    public sealed class BattleDefinition
    {
        private readonly ActorSpawnSpec[] _actors;

        public BattleDefinition(
            int tickRate,
            uint seed,
            int arenaHalfExtentMm,
            int spatialCellSizeMm,
            ActorSpawnSpec[] actors)
        {
            if (tickRate <= 0 || tickRate > 240)
            {
                throw new ArgumentOutOfRangeException(nameof(tickRate));
            }

            if (arenaHalfExtentMm <= 0 || arenaHalfExtentMm > 10_000_000)
            {
                throw new ArgumentOutOfRangeException(nameof(arenaHalfExtentMm));
            }

            if (spatialCellSizeMm <= 0 || spatialCellSizeMm > arenaHalfExtentMm * 2)
            {
                throw new ArgumentOutOfRangeException(nameof(spatialCellSizeMm));
            }

            if (actors == null || actors.Length == 0 || actors.Length > 100_000)
            {
                throw new ArgumentException("A battle needs between 1 and 100,000 actors.", nameof(actors));
            }

            TickRate = tickRate;
            Seed = seed;
            ArenaHalfExtentMm = arenaHalfExtentMm;
            SpatialCellSizeMm = spatialCellSizeMm;
            _actors = (ActorSpawnSpec[])actors.Clone();
            for (int i = 0; i < _actors.Length; i++)
            {
                Int2 position = _actors[i].PositionMm;
                if (position.X < -arenaHalfExtentMm
                    || position.X > arenaHalfExtentMm
                    || position.Y < -arenaHalfExtentMm
                    || position.Y > arenaHalfExtentMm)
                {
                    throw new ArgumentException(
                        "Actor " + i + " starts outside the arena.",
                        nameof(actors));
                }
            }
        }

        public int TickRate { get; }
        public uint Seed { get; }
        public int ArenaHalfExtentMm { get; }
        public int SpatialCellSizeMm { get; }
        public int ActorCount => _actors.Length;

        public ActorSpawnSpec GetActor(int index)
        {
            if ((uint)index >= (uint)_actors.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return _actors[index];
        }

        public ActorSpawnSpec[] CopyActors()
        {
            return (ActorSpawnSpec[])_actors.Clone();
        }

        public FixedStepCombatSimulation CreateSimulation(EventBus? events = null)
        {
            return new FixedStepCombatSimulation(this, events ?? new EventBus());
        }
    }
}
