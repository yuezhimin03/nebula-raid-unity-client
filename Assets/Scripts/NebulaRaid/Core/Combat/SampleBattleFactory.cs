using System;

namespace NebulaRaid.Combat
{
    public static class SampleBattleFactory
    {
        public static BattleDefinition CreateSkirmish(int actorsPerTeam = 4)
        {
            if (actorsPerTeam <= 0 || actorsPerTeam > 64)
            {
                throw new ArgumentOutOfRangeException(nameof(actorsPerTeam));
            }

            ActorSpawnSpec[] actors = new ActorSpawnSpec[actorsPerTeam * 2];
            for (int i = 0; i < actorsPerTeam; i++)
            {
                int laneY = (i - (actorsPerTeam / 2)) * 1_500;
                actors[i] = new ActorSpawnSpec(
                    1,
                    new Int2(-8_000 - (i * 250), laneY),
                    120,
                    180,
                    18,
                    2_200,
                    6);
                actors[actorsPerTeam + i] = new ActorSpawnSpec(
                    2,
                    new Int2(8_000 + (i * 250), -laneY),
                    120,
                    180,
                    18,
                    2_200,
                    6);
            }

            return new BattleDefinition(
                tickRate: 30,
                seed: 0xC0FFEEu,
                arenaHalfExtentMm: 20_000,
                spatialCellSizeMm: 2_000,
                actors: actors);
        }
    }
}

