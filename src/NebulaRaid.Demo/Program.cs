using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NebulaRaid.Combat;
using NebulaRaid.Messaging;
using NebulaRaid.Pooling;
using NebulaRaid.Replay;
using NebulaRaid.Resources;

namespace NebulaRaid.Demo
{
    internal static class Program
    {
        public static async Task<int> Main(string[] args)
        {
            try
            {
                DemoOptions options = DemoOptions.Parse(args);
                Directory.CreateDirectory(Path.GetDirectoryName(options.OutputPath) ?? ".");

                BattleDefinition definition = SampleBattleFactory.CreateSkirmish(4);
                EventBus events = new EventBus();
                ObjectPool<DamagePopup> popupPool = new ObjectPool<DamagePopup>(
                    () => new DamagePopup(),
                    popup => popup.Reset(),
                    maxRetained: 32,
                    initialCapacity: 8);
                int deaths = 0;
                using IDisposable attackSubscription = events.Subscribe<AttackResolved>(attack =>
                {
                    DamagePopup popup = popupPool.Rent();
                    popup.Set(attack.TargetId, attack.Damage);
                    popupPool.Return(popup);
                });
                using IDisposable deathSubscription = events.Subscribe<ActorDied>(_ => deaths++);

                FixedStepCombatSimulation simulation = definition.CreateSimulation(events);
                ReplayRecorder recorder = new ReplayRecorder(definition);
                while (simulation.Tick < options.MaximumTicks
                    && simulation.CountAlive(1) > 0
                    && simulation.CountAlive(2) > 0)
                {
                    InputCommand[] commands = DeterministicBot.BuildCommands(simulation);
                    int tick = simulation.Tick;
                    simulation.Step(commands);
                    recorder.Record(tick, commands, simulation.ComputeChecksum());
                }

                ReplayData replay = recorder.Finish();
                string serialized = ReplayCodec.Serialize(replay);
                File.WriteAllText(options.OutputPath, serialized);
                ReplayData parsedReplay = ReplayCodec.Parse(File.ReadAllText(options.OutputPath));
                ReplayVerificationResult verification = ReplayVerifier.Verify(parsedReplay);
                ResourceCacheMetrics cacheMetrics = await RunResourceCacheDemo();
                ObjectPoolMetrics poolMetrics = popupPool.GetMetrics();

                Console.WriteLine("Nebula Raid deterministic client demo");
                Console.WriteLine(
                    "battle: {0} actors, {1} Hz, {2} simulated ticks",
                    definition.ActorCount,
                    definition.TickRate,
                    simulation.Tick);
                Console.WriteLine(
                    "result: team1={0} alive, team2={1} alive, deaths={2}",
                    simulation.CountAlive(1),
                    simulation.CountAlive(2),
                    deaths);
                Console.WriteLine(
                    "final checksum: 0x{0:X16}",
                    simulation.ComputeChecksum());
                Console.WriteLine(
                    "replay: {0} ({1} frames, verify={2})",
                    Path.GetFullPath(options.OutputPath),
                    replay.Frames.Length,
                    verification.IsValid ? "PASS" : "FAIL");
                Console.WriteLine(
                    "pool: created={0}, rents={1}, retained={2}",
                    poolMetrics.Created,
                    poolMetrics.Rented,
                    poolMetrics.Retained);
                Console.WriteLine(
                    "resource cache: hits={0}, misses={1}, evictions={2}, resident={3}/{4} bytes",
                    cacheMetrics.Hits,
                    cacheMetrics.Misses,
                    cacheMetrics.Evictions,
                    cacheMetrics.ResidentWeight,
                    cacheMetrics.Budget);
                return verification.IsValid ? 0 : 2;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 1;
            }
        }

        private static async Task<ResourceCacheMetrics> RunResourceCacheDemo()
        {
            MemoryTextLoader loader = new MemoryTextLoader(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    { "ship", "SHIP01" },
                    { "laser", "LASER02" },
                });
            using ReferenceCountedResourceCache<TextAsset> cache =
                new ReferenceCountedResourceCache<TextAsset>(
                    loader,
                    budget: 8,
                    asset => asset.Text.Length);

            using (ResourceLease<TextAsset> first =
                await cache.AcquireAsync("ship", CancellationToken.None))
            using (ResourceLease<TextAsset> second =
                await cache.AcquireAsync("ship", CancellationToken.None))
            {
                if (!ReferenceEquals(first.Value, second.Value))
                {
                    throw new InvalidOperationException("Cache did not coalesce the resource.");
                }
            }

            using (await cache.AcquireAsync("laser", CancellationToken.None))
            {
            }

            return cache.GetMetrics();
        }

        private sealed class DamagePopup
        {
            public int TargetId { get; private set; }
            public int Damage { get; private set; }

            public void Set(int targetId, int damage)
            {
                TargetId = targetId;
                Damage = damage;
            }

            public void Reset()
            {
                TargetId = 0;
                Damage = 0;
            }
        }

        private sealed class TextAsset
        {
            public TextAsset(string text)
            {
                Text = text;
            }

            public string Text { get; }
        }

        private sealed class MemoryTextLoader : IResourceLoader<TextAsset>
        {
            private readonly Dictionary<string, string> _values;

            public MemoryTextLoader(Dictionary<string, string> values)
            {
                _values = values;
            }

            public Task<TextAsset> LoadAsync(string key, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!_values.TryGetValue(key, out string? value))
                {
                    throw new KeyNotFoundException(key);
                }

                return Task.FromResult(new TextAsset(value));
            }
        }

        private readonly struct DemoOptions
        {
            private DemoOptions(int maximumTicks, string outputPath)
            {
                MaximumTicks = maximumTicks;
                OutputPath = outputPath;
            }

            public int MaximumTicks { get; }
            public string OutputPath { get; }

            public static DemoOptions Parse(string[] args)
            {
                int ticks = 900;
                string output = Path.Combine(".artifacts", "demo", "last-match.nrr");
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i] == "--ticks" && i + 1 < args.Length)
                    {
                        if (!int.TryParse(
                            args[++i],
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out ticks)
                            || ticks <= 0
                            || ticks > 1_000_000)
                        {
                            throw new ArgumentException("--ticks must be between 1 and 1,000,000.");
                        }
                    }
                    else if (args[i] == "--output" && i + 1 < args.Length)
                    {
                        output = args[++i];
                    }
                    else
                    {
                        throw new ArgumentException("Usage: --ticks N --output PATH");
                    }
                }

                return new DemoOptions(ticks, output);
            }
        }
    }
}

