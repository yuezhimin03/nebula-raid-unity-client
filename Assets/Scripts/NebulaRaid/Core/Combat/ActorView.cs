namespace NebulaRaid.Combat
{
    public readonly struct ActorView
    {
        internal ActorView(
            int entityId,
            bool isAlive,
            byte team,
            Int2 positionMm,
            int health,
            int maxHealth,
            int attackCooldownRemaining)
        {
            EntityId = entityId;
            IsAlive = isAlive;
            Team = team;
            PositionMm = positionMm;
            Health = health;
            MaxHealth = maxHealth;
            AttackCooldownRemaining = attackCooldownRemaining;
        }

        public int EntityId { get; }
        public bool IsAlive { get; }
        public byte Team { get; }
        public Int2 PositionMm { get; }
        public int Health { get; }
        public int MaxHealth { get; }
        public int AttackCooldownRemaining { get; }
    }
}

