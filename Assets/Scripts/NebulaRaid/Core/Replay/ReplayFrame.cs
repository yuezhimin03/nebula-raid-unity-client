using System;
using NebulaRaid.Combat;

namespace NebulaRaid.Replay
{
    public sealed class ReplayFrame
    {
        public ReplayFrame(int tick, InputCommand[] commands, ulong postStepChecksum)
        {
            if (tick < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(tick));
            }

            Tick = tick;
            Commands = commands == null
                ? throw new ArgumentNullException(nameof(commands))
                : (InputCommand[])commands.Clone();
            PostStepChecksum = postStepChecksum;
        }

        public int Tick { get; }
        public InputCommand[] Commands { get; }
        public ulong PostStepChecksum { get; }
    }
}

