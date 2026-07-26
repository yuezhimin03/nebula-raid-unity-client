using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NebulaRaid.Combat;
using NebulaRaid.HotUpdate;
using NebulaRaid.Messaging;
using NebulaRaid.Pooling;
using NebulaRaid.Replay;
using NebulaRaid.Resources;

namespace NebulaRaid.Tests
{
    internal static class Program
    {
        public static async Task<int> Main()
        {
            TestCase[] tests =
            {
                new TestCase("combat/same-inputs-same-checksum", CombatIsDeterministic),
                new TestCase("combat/simultaneous-damage", SimultaneousDamageIsOrderIndependent),
                new TestCase("combat/reject-noncanonical-commands", RejectsNonCanonicalCommands),
                new TestCase("replay/roundtrip-and-tamper", ReplayRoundTripAndTamperDetection),
                new TestCase("messaging/subscription-order", EventBusPreservesOrder),
                new TestCase("pooling/reuse-and-reset", ObjectPoolReusesAndResets),
                new TestCase("resources/coalesce-refcount-lru", ResourceCacheCoalescesAndEvicts),
                new TestCase("hot-update/strict-manifest", ManifestParserIsStrict),
                new TestCase("hot-update/repository-samples", RepositorySamplesVerify),
                new TestCase("hot-update/verify-activate-rollback", HotUpdateBoundaryRollsBack),
            };

            int failures = 0;
            Stopwatch suite = Stopwatch.StartNew();
            for (int i = 0; i < tests.Length; i++)
            {
                Stopwatch timer = Stopwatch.StartNew();
                try
                {
                    await tests[i].Body();
                    timer.Stop();
                    Console.WriteLine(
                        "[PASS] {0} ({1:F1} ms)",
                        tests[i].Name,
                        timer.Elapsed.TotalMilliseconds);
                }
                catch (Exception exception)
                {
                    failures++;
                    timer.Stop();
                    Console.WriteLine(
                        "[FAIL] {0} ({1:F1} ms)",
                        tests[i].Name,
                        timer.Elapsed.TotalMilliseconds);
                    Console.WriteLine(exception);
                }
            }

            suite.Stop();
            Console.WriteLine(
                "summary: total={0}, passed={1}, failed={2}, elapsed={3:F1} ms",
                tests.Length,
                tests.Length - failures,
                failures,
                suite.Elapsed.TotalMilliseconds);
            return failures == 0 ? 0 : 1;
        }

        private static Task CombatIsDeterministic()
        {
            BattleDefinition definition = SampleBattleFactory.CreateSkirmish(5);
            FixedStepCombatSimulation left = definition.CreateSimulation();
            FixedStepCombatSimulation right = definition.CreateSimulation();

            for (int tick = 0; tick < 240; tick++)
            {
                InputCommand[] leftCommands = DeterministicBot.BuildCommands(left);
                InputCommand[] rightCommands = DeterministicBot.BuildCommands(right);
                AssertEx.Equal(leftCommands.Length, rightCommands.Length, "command count");
                left.Step(leftCommands);
                right.Step(rightCommands);
                AssertEx.Equal(left.ComputeChecksum(), right.ComputeChecksum(), "tick checksum");
            }

            AssertEx.True(left.TotalAttacksResolved > 0, "scenario should exercise combat");

            FixedStepCombatSimulation golden =
                SampleBattleFactory.CreateSkirmish(4).CreateSimulation();
            while (golden.CountAlive(1) > 0 && golden.CountAlive(2) > 0)
            {
                golden.Step(DeterministicBot.BuildCommands(golden));
            }

            AssertEx.Equal(88, golden.Tick, "golden replay length");
            AssertEx.Equal(
                0x9B6F5D132EEE944FUL,
                golden.ComputeChecksum(),
                "cross-run golden checksum");
            return Task.CompletedTask;
        }

        private static Task SimultaneousDamageIsOrderIndependent()
        {
            ActorSpawnSpec[] actors =
            {
                new ActorSpawnSpec(1, new Int2(-100, 0), 10, 0, 10, 1_000, 1),
                new ActorSpawnSpec(2, new Int2(100, 0), 10, 0, 10, 1_000, 1),
            };
            FixedStepCombatSimulation simulation = new BattleDefinition(
                30,
                1,
                2_000,
                500,
                actors).CreateSimulation();
            simulation.Step(new[]
            {
                new InputCommand(0, 0, 0, 0, InputCommand.PrimaryAbility),
                new InputCommand(0, 1, 0, 0, InputCommand.PrimaryAbility),
            });

            AssertEx.False(simulation.GetActor(0).IsAlive, "first actor should die");
            AssertEx.False(simulation.GetActor(1).IsAlive, "second actor should die");
            AssertEx.Equal(2L, simulation.TotalAttacksResolved, "both planned attacks resolve");
            return Task.CompletedTask;
        }

        private static Task RejectsNonCanonicalCommands()
        {
            FixedStepCombatSimulation simulation =
                SampleBattleFactory.CreateSkirmish(2).CreateSimulation();
            ulong checksumBefore = simulation.ComputeChecksum();
            AssertEx.Throws<InvalidOperationException>(() => simulation.Step(new[]
            {
                new InputCommand(0, 2, 0, 0, 0),
                new InputCommand(0, 1, 0, 0, 0),
            }));
            AssertEx.Equal(0, simulation.Tick, "rejected input must not advance tick");
            AssertEx.Equal(
                checksumBefore,
                simulation.ComputeChecksum(),
                "rejected input must not mutate authoritative state");
            return Task.CompletedTask;
        }

        private static Task ReplayRoundTripAndTamperDetection()
        {
            BattleDefinition definition = SampleBattleFactory.CreateSkirmish(3);
            FixedStepCombatSimulation simulation = definition.CreateSimulation();
            ReplayRecorder recorder = new ReplayRecorder(definition);
            for (int tick = 0; tick < 180; tick++)
            {
                InputCommand[] commands = DeterministicBot.BuildCommands(simulation);
                simulation.Step(commands);
                recorder.Record(tick, commands, simulation.ComputeChecksum());
            }

            ReplayData replay = recorder.Finish();
            ReplayData parsed = ReplayCodec.Parse(ReplayCodec.Serialize(replay));
            ReplayVerificationResult verification = ReplayVerifier.Verify(parsed);
            AssertEx.True(verification.IsValid, verification.Message);
            AssertEx.Equal(180, verification.CheckedFrames, "verified frame count");

            ReplayFrame[] tamperedFrames = (ReplayFrame[])parsed.Frames.Clone();
            ReplayFrame original = tamperedFrames[12];
            tamperedFrames[12] = new ReplayFrame(
                original.Tick,
                original.Commands,
                original.PostStepChecksum ^ 1UL);
            ReplayVerificationResult tampered = ReplayVerifier.Verify(
                new ReplayData(parsed.Definition, tamperedFrames));
            AssertEx.False(tampered.IsValid, "tampered checksum must fail");
            AssertEx.Equal(12, tampered.MismatchTick, "mismatch tick");
            return Task.CompletedTask;
        }

        private static Task EventBusPreservesOrder()
        {
            EventBus bus = new EventBus();
            List<string> calls = new List<string>();
            IDisposable first = bus.Subscribe<int>(value => calls.Add("first:" + value));
            using IDisposable second = bus.Subscribe<int>(value => calls.Add("second:" + value));
            bus.Publish(7);
            first.Dispose();
            bus.Publish(8);
            AssertEx.SequenceEqual(
                new[] { "first:7", "second:7", "second:8" },
                calls,
                "event order");
            return Task.CompletedTask;
        }

        private static Task ObjectPoolReusesAndResets()
        {
            ObjectPool<PooledBuffer> pool = new ObjectPool<PooledBuffer>(
                () => new PooledBuffer(),
                buffer => buffer.Value = 0,
                maxRetained: 1,
                initialCapacity: 1);
            PooledBuffer first = pool.Rent();
            first.Value = 42;
            pool.Return(first);
            PooledBuffer second = pool.Rent();
            AssertEx.True(ReferenceEquals(first, second), "pool should return retained instance");
            AssertEx.Equal(0, second.Value, "reset callback");
            pool.Return(second);
            ObjectPoolMetrics metrics = pool.GetMetrics();
            AssertEx.Equal(1L, metrics.Created, "created count");
            AssertEx.Equal(2L, metrics.Rented, "rent count");
            return Task.CompletedTask;
        }

        private static async Task ResourceCacheCoalescesAndEvicts()
        {
            DelayedStringLoader loader = new DelayedStringLoader();
            List<string> evicted = new List<string>();
            using ReferenceCountedResourceCache<CachedString> cache =
                new ReferenceCountedResourceCache<CachedString>(
                    loader,
                    budget: 8,
                    value => value.Value.Length,
                    value => evicted.Add(value.Value));

            Task<ResourceLease<CachedString>> firstTask = cache.AcquireAsync("aaaa");
            Task<ResourceLease<CachedString>> secondTask = cache.AcquireAsync("aaaa");
            using (ResourceLease<CachedString> first = await firstTask)
            using (ResourceLease<CachedString> second = await secondTask)
            {
                AssertEx.True(ReferenceEquals(first.Value, second.Value), "shared load");
                AssertEx.Equal(1, loader.GetCount("aaaa"), "one underlying load");
            }

            using (await cache.AcquireAsync("bbbbbb"))
            {
            }

            ResourceCacheMetrics afterEviction = cache.GetMetrics();
            AssertEx.Equal(1L, afterEviction.Evictions, "LRU eviction count");
            AssertEx.SequenceEqual(new[] { "aaaa" }, evicted, "evicted resource");

            using (await cache.AcquireAsync("aaaa"))
            {
            }

            AssertEx.Equal(2, loader.GetCount("aaaa"), "evicted asset should load again");
            ResourceCacheMetrics final = cache.GetMetrics();
            AssertEx.Equal(1L, final.Hits, "cache hit count");
            AssertEx.Equal(3L, final.Misses, "cache miss count");
        }

        private static Task ManifestParserIsStrict()
        {
            const string valid =
                "{\"schemaVersion\":1,\"bundleVersion\":\"1.2.3\","
                + "\"minimumAppVersion\":\"1.0.0\",\"entrypoint\":\"main.lua\","
                + "\"files\":[{\"path\":\"main.lua\","
                + "\"sha256\":\"0000000000000000000000000000000000000000000000000000000000000000\","
                + "\"size\":0}]}";
            HotUpdateManifest manifest = HotUpdateManifestCodec.Parse(valid);
            AssertEx.Equal("1.2.3", manifest.BundleVersion, "bundle version");
            AssertEx.Equal(1, manifest.Files.Length, "file count");
            AssertEx.Throws<FormatException>(() =>
                HotUpdateManifestCodec.Parse(valid.Replace(
                    "\"size\":0",
                    "\"size\":0,\"unexpected\":true")));
            AssertEx.Throws<FormatException>(() =>
                HotUpdateManifestCodec.Parse(valid.Replace(
                    "\"schemaVersion\":1",
                    "\"schemaVersion\":1,\"schemaVersion\":1")));
            return Task.CompletedTask;
        }

        private static Task HotUpdateBoundaryRollsBack()
        {
            string testRoot = Path.Combine(
                Path.GetTempPath(),
                "NebulaRaid.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testRoot);
            try
            {
                string packages = Path.Combine(testRoot, "packages");
                string install = Path.Combine(testRoot, "install");
                string v1 = CreatePackage(packages, "1.0.0", "return { value = 1 }\n");
                string v2 = CreatePackage(packages, "1.1.0", "return { value = 2 }\n");
                string rejected = CreatePackage(packages, "1.2.0", "return { reject = true }\n");
                string tampered = CreatePackage(packages, "1.3.0", "return { value = 3 }\n");
                File.AppendAllText(Path.Combine(tampered, "main.lua"), "-- tampered");

                HotUpdatePackageVerifier verifier = new HotUpdatePackageVerifier(
                    new HotUpdatePolicy(
                        "1.0.0",
                        maximumFiles: 8,
                        maximumFileBytes: 1_024,
                        maximumBundleBytes: 4_096));
                HotUpdateReleaseStore store = new HotUpdateReleaseStore(
                    install,
                    verifier,
                    new ContentProbe());

                AssertEx.True(store.InstallAndActivate(v1).Succeeded, "activate v1");
                AssertEx.Equal("1.0.0", store.GetActiveVersion(), "v1 active");
                AssertEx.True(store.InstallAndActivate(v2).Succeeded, "activate v2");
                AssertEx.Equal("1.1.0", store.GetActiveVersion(), "v2 active");
                AssertEx.True(store.Rollback().Succeeded, "rollback");
                AssertEx.Equal("1.0.0", store.GetActiveVersion(), "rolled back to v1");

                HotUpdateActivationResult runtimeRejected =
                    store.InstallAndActivate(rejected);
                AssertEx.False(runtimeRejected.Succeeded, "runtime probe rejection");
                AssertEx.Equal("1.0.0", store.GetActiveVersion(), "pointer unchanged");
                AssertEx.False(verifier.Verify(tampered).IsValid, "hash tamper rejection");

                string unsafePackage = Path.Combine(packages, "unsafe");
                Directory.CreateDirectory(unsafePackage);
                File.WriteAllText(
                    Path.Combine(unsafePackage, "manifest.json"),
                    "{\"schemaVersion\":1,\"bundleVersion\":\"1.4.0\","
                    + "\"minimumAppVersion\":\"1.0.0\",\"entrypoint\":\"../escape.lua\","
                    + "\"files\":[{\"path\":\"../escape.lua\","
                    + "\"sha256\":\"0000000000000000000000000000000000000000000000000000000000000000\","
                    + "\"size\":0}]}",
                    new UTF8Encoding(false));
                AssertEx.False(verifier.Verify(unsafePackage).IsValid, "path traversal rejection");
            }
            finally
            {
                if (Directory.Exists(testRoot))
                {
                    Directory.Delete(testRoot, true);
                }
            }

            return Task.CompletedTask;
        }

        private static Task RepositorySamplesVerify()
        {
            string root = FindRepositoryRoot();
            string samples = Path.Combine(
                root,
                "Assets",
                "StreamingAssets",
                "HotUpdateSamples");
            HotUpdatePackageVerifier verifier = new HotUpdatePackageVerifier(
                new HotUpdatePolicy("1.0.0"));
            PackageVerificationResult first = verifier.Verify(Path.Combine(samples, "1.0.0"));
            PackageVerificationResult second = verifier.Verify(Path.Combine(samples, "1.1.0"));
            AssertEx.True(first.IsValid, "sample 1.0.0: " + first.Message);
            AssertEx.True(second.IsValid, "sample 1.1.0: " + second.Message);
            return Task.CompletedTask;
        }

        private static string CreatePackage(
            string packagesRoot,
            string version,
            string scriptContent)
        {
            string package = Path.Combine(packagesRoot, version);
            Directory.CreateDirectory(package);
            string scriptPath = Path.Combine(package, "main.lua");
            File.WriteAllText(scriptPath, scriptContent, new UTF8Encoding(false));
            byte[] hash;
            using (FileStream stream = File.OpenRead(scriptPath))
            using (SHA256 sha256 = SHA256.Create())
            {
                hash = sha256.ComputeHash(stream);
            }

            StringBuilder hex = new StringBuilder(64);
            for (int i = 0; i < hash.Length; i++)
            {
                hex.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
            }

            long size = new FileInfo(scriptPath).Length;
            string manifest =
                "{\"schemaVersion\":1,\"bundleVersion\":\"" + version + "\","
                + "\"minimumAppVersion\":\"1.0.0\",\"entrypoint\":\"main.lua\","
                + "\"files\":[{\"path\":\"main.lua\",\"sha256\":\"" + hex + "\","
                + "\"size\":" + size.ToString(CultureInfo.InvariantCulture) + "}]}";
            File.WriteAllText(
                Path.Combine(package, "manifest.json"),
                manifest,
                new UTF8Encoding(false));
            return package;
        }

        private static string FindRepositoryRoot()
        {
            DirectoryInfo? cursor = new DirectoryInfo(AppContext.BaseDirectory);
            while (cursor != null)
            {
                if (File.Exists(Path.Combine(cursor.FullName, "NebulaRaid.sln")))
                {
                    return cursor.FullName;
                }

                cursor = cursor.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate NebulaRaid.sln.");
        }

        private readonly struct TestCase
        {
            public TestCase(string name, Func<Task> body)
            {
                Name = name;
                Body = body;
            }

            public string Name { get; }
            public Func<Task> Body { get; }
        }

        private sealed class PooledBuffer
        {
            public int Value { get; set; }
        }

        private sealed class CachedString
        {
            public CachedString(string value)
            {
                Value = value;
            }

            public string Value { get; }
        }

        private sealed class DelayedStringLoader : IResourceLoader<CachedString>
        {
            private readonly object _gate = new object();
            private readonly Dictionary<string, int> _counts =
                new Dictionary<string, int>(StringComparer.Ordinal);

            public async Task<CachedString> LoadAsync(
                string key,
                CancellationToken cancellationToken)
            {
                lock (_gate)
                {
                    _counts.TryGetValue(key, out int count);
                    _counts[key] = count + 1;
                }

                await Task.Delay(5, cancellationToken);
                return new CachedString(key);
            }

            public int GetCount(string key)
            {
                lock (_gate)
                {
                    return _counts.TryGetValue(key, out int count) ? count : 0;
                }
            }
        }

        private sealed class ContentProbe : ILuaRuntimeProbe
        {
            public bool CanLoad(
                string releaseDirectory,
                string entrypointRelativePath,
                out string failureReason)
            {
                string content = File.ReadAllText(
                    Path.Combine(
                        releaseDirectory,
                        entrypointRelativePath.Replace('/', Path.DirectorySeparatorChar)));
                if (content.IndexOf("reject", StringComparison.Ordinal) >= 0)
                {
                    failureReason = "Synthetic probe rejected script content.";
                    return false;
                }

                failureReason = string.Empty;
                return true;
            }
        }
    }

    internal static class AssertEx
    {
        public static void True(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException("Assertion failed: " + message);
            }
        }

        public static void False(bool condition, string message)
        {
            True(!condition, message);
        }

        public static void Equal<T>(T expected, T actual, string message)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException(
                    "Assertion failed: " + message + ". Expected " + expected + ", got " + actual + ".");
            }
        }

        public static void SequenceEqual<T>(
            IReadOnlyList<T> expected,
            IReadOnlyList<T> actual,
            string message)
        {
            if (expected.Count != actual.Count)
            {
                throw new InvalidOperationException(
                    "Assertion failed: " + message + ". Sequence lengths differ.");
            }

            for (int i = 0; i < expected.Count; i++)
            {
                if (!EqualityComparer<T>.Default.Equals(expected[i], actual[i]))
                {
                    throw new InvalidOperationException(
                        "Assertion failed: " + message + " at index " + i + ".");
                }
            }
        }

        public static void Throws<T>(Action action) where T : Exception
        {
            try
            {
                action();
            }
            catch (T)
            {
                return;
            }

            throw new InvalidOperationException(
                "Assertion failed: expected " + typeof(T).Name + ".");
        }
    }
}
