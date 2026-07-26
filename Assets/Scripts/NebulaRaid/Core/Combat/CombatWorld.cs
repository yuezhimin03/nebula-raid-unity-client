using System;

namespace NebulaRaid.Combat
{
    /// <summary>
    /// ECS-lite world: each component is a dense array indexed by stable entity id.
    /// No managed component object is created per actor.
    /// </summary>
    internal sealed class CombatWorld
    {
        public CombatWorld(BattleDefinition definition)
        {
            Count = definition.ActorCount;
            Alive = new bool[Count];
            Team = new byte[Count];
            PositionX = new int[Count];
            PositionY = new int[Count];
            Health = new int[Count];
            MaxHealth = new int[Count];
            Speed = new int[Count];
            Damage = new int[Count];
            AttackRange = new int[Count];
            AttackCooldownTicks = new int[Count];
            AttackCooldownRemaining = new int[Count];
            MoveX = new sbyte[Count];
            MoveY = new sbyte[Count];

            for (int entityId = 0; entityId < Count; entityId++)
            {
                ActorSpawnSpec spec = definition.GetActor(entityId);
                Alive[entityId] = true;
                Team[entityId] = spec.Team;
                PositionX[entityId] = spec.PositionMm.X;
                PositionY[entityId] = spec.PositionMm.Y;
                Health[entityId] = spec.MaxHealth;
                MaxHealth[entityId] = spec.MaxHealth;
                Speed[entityId] = spec.SpeedMmPerTick;
                Damage[entityId] = spec.Damage;
                AttackRange[entityId] = spec.AttackRangeMm;
                AttackCooldownTicks[entityId] = spec.AttackCooldownTicks;
            }
        }

        public int Count { get; }
        public bool[] Alive { get; }
        public byte[] Team { get; }
        public int[] PositionX { get; }
        public int[] PositionY { get; }
        public int[] Health { get; }
        public int[] MaxHealth { get; }
        public int[] Speed { get; }
        public int[] Damage { get; }
        public int[] AttackRange { get; }
        public int[] AttackCooldownTicks { get; }
        public int[] AttackCooldownRemaining { get; }
        public sbyte[] MoveX { get; }
        public sbyte[] MoveY { get; }

        public ActorView GetActorView(int entityId)
        {
            if ((uint)entityId >= (uint)Count)
            {
                throw new ArgumentOutOfRangeException(nameof(entityId));
            }

            return new ActorView(
                entityId,
                Alive[entityId],
                Team[entityId],
                new Int2(PositionX[entityId], PositionY[entityId]),
                Health[entityId],
                MaxHealth[entityId],
                AttackCooldownRemaining[entityId]);
        }
    }
}

