using System;

namespace NebulaRaid.Combat
{
    public readonly struct InputCommand
    {
        public const byte PrimaryAbility = 1;

        public InputCommand(int tick, int entityId, sbyte moveX, sbyte moveY, byte abilityMask)
        {
            if (tick < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(tick));
            }

            if (entityId < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(entityId));
            }

            if (moveX < -1 || moveX > 1)
            {
                throw new ArgumentOutOfRangeException(nameof(moveX));
            }

            if (moveY < -1 || moveY > 1)
            {
                throw new ArgumentOutOfRangeException(nameof(moveY));
            }

            if ((abilityMask & ~PrimaryAbility) != 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(abilityMask),
                    "Unknown ability bits are not allowed.");
            }

            Tick = tick;
            EntityId = entityId;
            MoveX = moveX;
            MoveY = moveY;
            AbilityMask = abilityMask;
        }

        public int Tick { get; }
        public int EntityId { get; }
        public sbyte MoveX { get; }
        public sbyte MoveY { get; }
        public byte AbilityMask { get; }
    }
}
