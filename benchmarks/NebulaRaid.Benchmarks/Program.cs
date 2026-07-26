using System;
using System.Diagnostics;
using System.Globalization;
using NebulaRaid.Combat;

namespace NebulaRaid.Benchmarks
{
    internal static class Program
    {
        public static int Main(string[] args)
        {
            try
            {
                Options options = Options.Parse(args);
                const int warmupTicks = 20;
                BattleDefinition definition = CreatePairedBattle(options.EntityCount);
                FixedStepCombatSimulation simulation = definition.CreateSimulation();
                InputCommand[][] commands = BuildCommands(
                    warmupTicks + options.MeasuredTicks,
                    options.EntityCount);

                for (int tick = 0; tick < warmupTicks; tick++)
                {
                    simulation.Step(commands[tick]);
                }

                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                int gen0Before = GC.CollectionCount(0);
                Stopwatch stopwatch = Stopwatch.StartNew();
                for (int tick = warmupTicks; tick < commands.Length; tick++)
                {
                    simulation.Step(commands[tick]);
                }

                stopwatch.Stop();
                long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
                int gen0Collections = GC.CollectionCount(0) - gen0Before;
                double ticksPerSecond = options.MeasuredTicks / stopwatch.Elapsed.TotalSeconds;
                double actorUpdatesPerSecond = ticksPerSecond * options.EntityCount;

                Console.WriteLine("Nebula Raid microbenchmark (not a Unity player benchmark)");
                Console.WriteLine(
                    "runtime={0}; os={1}; cpu={2}",
                    Environment.Version,
                    Environment.OSVersion,
                    Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "unknown");
                Console.WriteLine(
                    "entities={0}; warmupTicks={1}; measuredTicks={2}",
                    options.EntityCount,
                    warmupTicks,
                    options.MeasuredTicks);
                Console.WriteLine(
                    "elapsedMs={0:F3}; ticksPerSecond={1:F1}; actorStepsPerSecond={2:F0}",
                    stopwatch.Elapsed.TotalMilliseconds,
                    ticksPerSecond,
                    actorUpdatesPerSecond);
                Console.WriteLine(
                    "measuredThreadAllocBytes={0}; gen0Collections={1}; attacks={2}",
                    allocated,
                    gen0Collections,
                    simulation.TotalAttacksResolved);
                Console.WriteLine("checksum=0x{0:X16}", simulation.ComputeChecksum());
                Console.WriteLine(
                    "scope: timing covers Step only; command arrays and scenario setup are preallocated.");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception.Message);
                return 1;
            }
        }

        private static BattleDefinition CreatePairedBattle(int entityCount)
        {
            ActorSpawnSpec[] actors = new ActorSpawnSpec[entityCount];
            int pairCount = entityCount / 2;
            int columns = (int)Math.Ceiling(Math.Sqrt(pairCount));
            int spacing = 4_000;
            int halfExtent = Math.Max(10_000, (columns * spacing / 2) + 3_000);
            for (int pair = 0; pair < pairCount; pair++)
            {
                int column = pair % columns;
                int row = pair / columns;
                int x = (column * spacing) - ((columns - 1) * spacing / 2);
                int y = (row * spacing) - ((columns - 1) * spacing / 2);
                actors[pair * 2] = new ActorSpawnSpec(
                    1,
                    new Int2(x - 400, y),
                    10_000_000,
                    0,
                    1,
                    1_000,
                    1);
                actors[(pair * 2) + 1] = new ActorSpawnSpec(
                    2,
                    new Int2(x + 400, y),
                    10_000_000,
                    0,
                    1,
                    1_000,
                    1);
            }

            return new BattleDefinition(
                30,
                12345,
                halfExtent,
                1_000,
                actors);
        }

        private static InputCommand[][] BuildCommands(int ticks, int actors)
        {
            InputCommand[][] result = new InputCommand[ticks][];
            for (int tick = 0; tick < ticks; tick++)
            {
                InputCommand[] frame = new InputCommand[actors];
                for (int entity = 0; entity < actors; entity++)
                {
                    frame[entity] = new InputCommand(
                        tick,
                        entity,
                        0,
                        0,
                        InputCommand.PrimaryAbility);
                }

                result[tick] = frame;
            }

            return result;
        }

        private readonly struct Options
        {
            private Options(int entityCount, int measuredTicks)
            {
                EntityCount = entityCount;
                MeasuredTicks = measuredTicks;
            }

            public int EntityCount { get; }
            public int MeasuredTicks { get; }

            public static Options Parse(string[] args)
            {
                int entities = 1_024;
                int ticks = 300;
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i] == "--entities" && i + 1 < args.Length)
                    {
                        entities = ParsePositive(args[++i], "--entities", 100_000);
                    }
                    else if (args[i] == "--ticks" && i + 1 < args.Length)
                    {
                        ticks = ParsePositive(args[++i], "--ticks", 100_000);
                    }
                    else
                    {
                        throw new ArgumentException(
                            "Usage: --entities EVEN_NUMBER --ticks POSITIVE_NUMBER");
                    }
                }

                if (entities < 2 || entities % 2 != 0)
                {
                    throw new ArgumentException("--entities must be an even number of at least 2.");
                }

                return new Options(entities, ticks);
            }

            private static int ParsePositive(string value, string label, int maximum)
            {
                if (!int.TryParse(
                    value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int parsed)
                    || parsed <= 0
                    || parsed > maximum)
                {
                    throw new ArgumentException(label + " is outside the supported range.");
                }

                return parsed;
            }
        }
    }
}

