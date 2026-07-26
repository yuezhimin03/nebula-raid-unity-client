using System;
using NebulaRaid.Combat;

namespace NebulaRaid.Replay
{
    public static class ReplayVerifier
    {
        public static ReplayVerificationResult Verify(ReplayData replay)
        {
            if (replay == null)
            {
                throw new ArgumentNullException(nameof(replay));
            }

            FixedStepCombatSimulation simulation = replay.Definition.CreateSimulation();
            for (int i = 0; i < replay.Frames.Length; i++)
            {
                ReplayFrame frame = replay.Frames[i];
                if (frame.Tick != simulation.Tick)
                {
                    return ReplayVerificationResult.Mismatch(
                        i,
                        simulation.Tick,
                        frame.PostStepChecksum,
                        simulation.ComputeChecksum());
                }

                simulation.Step(frame.Commands);
                ulong actual = simulation.ComputeChecksum();
                if (actual != frame.PostStepChecksum)
                {
                    return ReplayVerificationResult.Mismatch(
                        i + 1,
                        frame.Tick,
                        frame.PostStepChecksum,
                        actual);
                }
            }

            return ReplayVerificationResult.Success(replay.Frames.Length);
        }
    }
}

