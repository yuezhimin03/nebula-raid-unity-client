using System;
using System.Collections.Generic;
using NebulaRaid.Combat;

namespace NebulaRaid.Replay
{
    public sealed class ReplayRecorder
    {
        private readonly BattleDefinition _definition;
        private readonly List<ReplayFrame> _frames = new List<ReplayFrame>();

        public ReplayRecorder(BattleDefinition definition)
        {
            _definition = definition ?? throw new ArgumentNullException(nameof(definition));
        }

        public int FrameCount => _frames.Count;

        public void Record(int tick, InputCommand[] commands, ulong postStepChecksum)
        {
            if (tick != _frames.Count)
            {
                throw new InvalidOperationException("Replay ticks must be contiguous and start at zero.");
            }

            _frames.Add(new ReplayFrame(tick, commands, postStepChecksum));
        }

        public ReplayData Finish()
        {
            return new ReplayData(_definition, _frames.ToArray());
        }
    }
}

