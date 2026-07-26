namespace NebulaRaid.Combat
{
    public readonly struct AttackResolved
    {
        public AttackResolved(int tick, int attackerId, int targetId, int damage)
        {
            Tick = tick;
            AttackerId = attackerId;
            TargetId = targetId;
            Damage = damage;
        }

        public int Tick { get; }
        public int AttackerId { get; }
        public int TargetId { get; }
        public int Damage { get; }
    }

    public readonly struct HealthChanged
    {
        public HealthChanged(int tick, int entityId, int previousHealth, int currentHealth)
        {
            Tick = tick;
            EntityId = entityId;
            PreviousHealth = previousHealth;
            CurrentHealth = currentHealth;
        }

        public int Tick { get; }
        public int EntityId { get; }
        public int PreviousHealth { get; }
        public int CurrentHealth { get; }
    }

    public readonly struct ActorDied
    {
        public ActorDied(int tick, int entityId, byte team)
        {
            Tick = tick;
            EntityId = entityId;
            Team = team;
        }

        public int Tick { get; }
        public int EntityId { get; }
        public byte Team { get; }
    }
}

