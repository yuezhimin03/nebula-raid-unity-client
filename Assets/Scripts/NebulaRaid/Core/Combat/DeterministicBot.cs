using System;
using System.Collections.Generic;

namespace NebulaRaid.Combat
{
    /// <summary>
    /// Small deterministic command source used by the demo and replay tests.
    /// Production input code can replace it without changing the simulation.
    /// </summary>
    public static class DeterministicBot
    {
        public static InputCommand[] BuildCommands(FixedStepCombatSimulation simulation)
        {
            if (simulation == null)
            {
                throw new ArgumentNullException(nameof(simulation));
            }

            List<InputCommand> commands = new List<InputCommand>(simulation.ActorCount);
            for (int entityId = 0; entityId < simulation.ActorCount; entityId++)
            {
                ActorView actor = simulation.GetActor(entityId);
                if (!actor.IsAlive)
                {
                    continue;
                }

                int targetId = FindNearestEnemy(simulation, actor);
                if (targetId < 0)
                {
                    commands.Add(new InputCommand(simulation.Tick, entityId, 0, 0, 0));
                    continue;
                }

                ActorView target = simulation.GetActor(targetId);
                sbyte moveX = Sign(target.PositionMm.X - actor.PositionMm.X);
                sbyte moveY = Sign(target.PositionMm.Y - actor.PositionMm.Y);
                commands.Add(new InputCommand(
                    simulation.Tick,
                    entityId,
                    moveX,
                    moveY,
                    InputCommand.PrimaryAbility));
            }

            return commands.ToArray();
        }

        private static int FindNearestEnemy(FixedStepCombatSimulation simulation, ActorView actor)
        {
            long nearestDistanceSquared = long.MaxValue;
            int nearestId = -1;
            for (int candidateId = 0; candidateId < simulation.ActorCount; candidateId++)
            {
                ActorView candidate = simulation.GetActor(candidateId);
                if (!candidate.IsAlive || candidate.Team == actor.Team)
                {
                    continue;
                }

                long dx = (long)candidate.PositionMm.X - actor.PositionMm.X;
                long dy = (long)candidate.PositionMm.Y - actor.PositionMm.Y;
                long distanceSquared = (dx * dx) + (dy * dy);
                if (distanceSquared < nearestDistanceSquared
                    || (distanceSquared == nearestDistanceSquared && candidateId < nearestId))
                {
                    nearestDistanceSquared = distanceSquared;
                    nearestId = candidateId;
                }
            }

            return nearestId;
        }

        private static sbyte Sign(int value)
        {
            if (value < 0)
            {
                return -1;
            }

            return value > 0 ? (sbyte)1 : (sbyte)0;
        }
    }
}

