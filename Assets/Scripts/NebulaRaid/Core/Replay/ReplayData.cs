using System;
using NebulaRaid.Combat;

namespace NebulaRaid.Replay
{
    public sealed class ReplayData
    {
        public ReplayData(BattleDefinition definition, ReplayFrame[] frames)
        {
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            Frames = frames == null
                ? throw new ArgumentNullException(nameof(frames))
                : (ReplayFrame[])frames.Clone();
        }

        public BattleDefinition Definition { get; }
        public ReplayFrame[] Frames { get; }
    }
}

