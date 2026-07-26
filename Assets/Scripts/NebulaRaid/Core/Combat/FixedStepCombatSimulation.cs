using System;
using NebulaRaid.Messaging;

namespace NebulaRaid.Combat
{
    /// <summary>
    /// Deterministic fixed-step simulation. It uses integer-only authoritative
    /// state and resolves planned damage simultaneously at the end of each tick.
    /// </summary>
    public sealed class FixedStepCombatSimulation
    {
        private readonly BattleDefinition _definition;
        private readonly CombatWorld _world;
        private readonly EventBus _events;
        private readonly DeterministicSpatialGrid _spatialGrid;
        private readonly bool[] _attackRequested;
        private readonly int[] _plannedTarget;
        private readonly int[] _pendingDamage;
        private long _totalAttacksResolved;

        internal FixedStepCombatSimulation(BattleDefinition definition, EventBus events)
        {
            _definition = definition;
            _events = events;
            _world = new CombatWorld(definition);
            _spatialGrid = new DeterministicSpatialGrid(definition.SpatialCellSizeMm);
            _attackRequested = new bool[_world.Count];
            _plannedTarget = new int[_world.Count];
            _pendingDamage = new int[_world.Count];
            for (int i = 0; i < _plannedTarget.Length; i++)
            {
                _plannedTarget[i] = -1;
            }
        }

        public int Tick { get; private set; }
        public int ActorCount => _world.Count;
        public long TotalAttacksResolved => _totalAttacksResolved;
        public EventBus Events => _events;

        public ActorView GetActor(int entityId)
        {
            return _world.GetActorView(entityId);
        }

        public int CountAlive(byte team)
        {
            int alive = 0;
            for (int i = 0; i < _world.Count; i++)
            {
                if (_world.Alive[i] && _world.Team[i] == team)
                {
                    alive++;
                }
            }

            return alive;
        }

        public void Step(InputCommand[] commands)
        {
            if (commands == null)
            {
                throw new ArgumentNullException(nameof(commands));
            }

            ValidateCommands(commands);
            PrepareTick();
            ApplyCommands(commands);
            MoveActors();
            _spatialGrid.Rebuild(_world);
            PlanAttacks();
            ResolveAttacks();
            Tick++;
        }

        public ulong ComputeChecksum()
        {
            StableHash64 hash = StableHash64.Create();
            hash.Add(Tick);
            hash.Add(_definition.TickRate);
            hash.Add(_definition.Seed);
            hash.Add(_definition.ArenaHalfExtentMm);
            hash.Add(_world.Count);

            for (int i = 0; i < _world.Count; i++)
            {
                hash.Add(i);
                hash.Add(_world.Alive[i]);
                hash.Add(_world.Team[i]);
                hash.Add(_world.PositionX[i]);
                hash.Add(_world.PositionY[i]);
                hash.Add(_world.Health[i]);
                hash.Add(_world.MaxHealth[i]);
                hash.Add(_world.Speed[i]);
                hash.Add(_world.Damage[i]);
                hash.Add(_world.AttackRange[i]);
                hash.Add(_world.AttackCooldownTicks[i]);
                hash.Add(_world.AttackCooldownRemaining[i]);
                hash.Add(_world.MoveX[i]);
                hash.Add(_world.MoveY[i]);
            }

            return hash.ToUInt64();
        }

        private void PrepareTick()
        {
            Array.Clear(_attackRequested, 0, _attackRequested.Length);
            Array.Clear(_pendingDamage, 0, _pendingDamage.Length);
            for (int i = 0; i < _world.Count; i++)
            {
                _plannedTarget[i] = -1;
                if (_world.Alive[i] && _world.AttackCooldownRemaining[i] > 0)
                {
                    _world.AttackCooldownRemaining[i]--;
                }
            }
        }

        private void ValidateCommands(InputCommand[] commands)
        {
            int previousEntityId = -1;
            for (int i = 0; i < commands.Length; i++)
            {
                InputCommand command = commands[i];
                if (command.Tick != Tick)
                {
                    throw new InvalidOperationException(
                        "Command tick " + command.Tick + " does not match simulation tick " + Tick + ".");
                }

                if (command.EntityId <= previousEntityId)
                {
                    throw new InvalidOperationException(
                        "Commands must be unique and sorted by ascending entity id.");
                }

                if ((uint)command.EntityId >= (uint)_world.Count)
                {
                    throw new InvalidOperationException("Command references an unknown entity.");
                }

                previousEntityId = command.EntityId;
            }
        }

        private void ApplyCommands(InputCommand[] commands)
        {
            for (int i = 0; i < commands.Length; i++)
            {
                InputCommand command = commands[i];
                _world.MoveX[command.EntityId] = command.MoveX;
                _world.MoveY[command.EntityId] = command.MoveY;
                _attackRequested[command.EntityId] =
                    (command.AbilityMask & InputCommand.PrimaryAbility) != 0;
            }
        }

        private void MoveActors()
        {
            int halfExtent = _definition.ArenaHalfExtentMm;
            for (int entityId = 0; entityId < _world.Count; entityId++)
            {
                if (!_world.Alive[entityId])
                {
                    continue;
                }

                long nextX = (long)_world.PositionX[entityId]
                    + ((long)_world.MoveX[entityId] * _world.Speed[entityId]);
                long nextY = (long)_world.PositionY[entityId]
                    + ((long)_world.MoveY[entityId] * _world.Speed[entityId]);
                _world.PositionX[entityId] = ClampToArena(nextX, halfExtent);
                _world.PositionY[entityId] = ClampToArena(nextY, halfExtent);
            }
        }

        private void PlanAttacks()
        {
            for (int attackerId = 0; attackerId < _world.Count; attackerId++)
            {
                if (!_world.Alive[attackerId]
                    || !_attackRequested[attackerId]
                    || _world.AttackCooldownRemaining[attackerId] > 0)
                {
                    continue;
                }

                int targetId = _spatialGrid.FindNearestEnemy(
                    _world,
                    attackerId,
                    _world.AttackRange[attackerId]);
                if (targetId < 0)
                {
                    continue;
                }

                _plannedTarget[attackerId] = targetId;
                _world.AttackCooldownRemaining[attackerId] =
                    _world.AttackCooldownTicks[attackerId];
                _pendingDamage[targetId] = SaturatingAdd(
                    _pendingDamage[targetId],
                    _world.Damage[attackerId]);
            }
        }

        private void ResolveAttacks()
        {
            for (int attackerId = 0; attackerId < _world.Count; attackerId++)
            {
                int targetId = _plannedTarget[attackerId];
                if (targetId < 0)
                {
                    continue;
                }

                _totalAttacksResolved++;
                _events.Publish(new AttackResolved(
                    Tick,
                    attackerId,
                    targetId,
                    _world.Damage[attackerId]));
            }

            for (int targetId = 0; targetId < _world.Count; targetId++)
            {
                int damage = _pendingDamage[targetId];
                if (damage <= 0 || !_world.Alive[targetId])
                {
                    continue;
                }

                int previousHealth = _world.Health[targetId];
                int currentHealth = damage >= previousHealth ? 0 : previousHealth - damage;
                _world.Health[targetId] = currentHealth;
                _events.Publish(new HealthChanged(Tick, targetId, previousHealth, currentHealth));

                if (currentHealth == 0)
                {
                    _world.Alive[targetId] = false;
                    _world.MoveX[targetId] = 0;
                    _world.MoveY[targetId] = 0;
                    _events.Publish(new ActorDied(Tick, targetId, _world.Team[targetId]));
                }
            }
        }

        private static int ClampToArena(long value, int halfExtent)
        {
            if (value < -halfExtent)
            {
                return -halfExtent;
            }

            if (value > halfExtent)
            {
                return halfExtent;
            }

            return (int)value;
        }

        private static int SaturatingAdd(int left, int right)
        {
            return left > int.MaxValue - right ? int.MaxValue : left + right;
        }
    }
}
