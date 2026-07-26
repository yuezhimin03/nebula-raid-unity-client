using System;

namespace NebulaRaid.Combat
{
    public readonly struct ActorSpawnSpec
    {
        public ActorSpawnSpec(
            byte team,
            Int2 positionMm,
            int maxHealth,
            int speedMmPerTick,
            int damage,
            int attackRangeMm,
            int attackCooldownTicks)
        {
            if (team == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(team), "Team 0 is reserved.");
            }

            if (maxHealth <= 0 || maxHealth > 10_000_000)
            {
                throw new ArgumentOutOfRangeException(nameof(maxHealth));
            }

            if (speedMmPerTick < 0 || speedMmPerTick > 100_000)
            {
                throw new ArgumentOutOfRangeException(nameof(speedMmPerTick));
            }

            if (damage < 0 || damage > 1_000_000)
            {
                throw new ArgumentOutOfRangeException(nameof(damage));
            }

            if (attackRangeMm <= 0 || attackRangeMm > 1_000_000)
            {
                throw new ArgumentOutOfRangeException(nameof(attackRangeMm));
            }

            if (attackCooldownTicks <= 0 || attackCooldownTicks > 100_000)
            {
                throw new ArgumentOutOfRangeException(nameof(attackCooldownTicks));
            }

            Team = team;
            PositionMm = positionMm;
            MaxHealth = maxHealth;
            SpeedMmPerTick = speedMmPerTick;
            Damage = damage;
            AttackRangeMm = attackRangeMm;
            AttackCooldownTicks = attackCooldownTicks;
        }

        public byte Team { get; }
        public Int2 PositionMm { get; }
        public int MaxHealth { get; }
        public int SpeedMmPerTick { get; }
        public int Damage { get; }
        public int AttackRangeMm { get; }
        public int AttackCooldownTicks { get; }
    }
}

