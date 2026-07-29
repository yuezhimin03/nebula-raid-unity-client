using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using NebulaRaid.Unity.Native;

namespace NebulaRaid.NativeInterop.Tests
{
    internal static class Program
    {
        public static int Main(string[] args)
        {
            try
            {
                string library = ParseLibraryPath(args);
                NativeLibrary.SetDllImportResolver(
                    typeof(NativeSimulationWorld).Assembly,
                    (name, assembly, searchPath) =>
                    {
                        if (name != "NebulaNative")
                        {
                            return IntPtr.Zero;
                        }
                        return NativeLibrary.Load(library);
                    });

                Assert(NativeSimulationWorld.AbiVersion == 1U, "ABI version");
                using (NativeSimulationWorld world = new NativeSimulationWorld(
                    new NativeWorldConfig
                    {
                        Capacity = 2U,
                        ArenaHalfExtentMm = 10_000,
                        GridCellSizeMm = 1_000,
                    }))
                {
                    uint first = world.Spawn(CreateSpawn(1U, -100));
                    uint second = world.Spawn(CreateSpawn(2U, 100));
                    world.Step(new[]
                    {
                        new NativeCommand
                        {
                            Tick = 0U,
                            ActorId = first,
                            Ability = NativeAbility.Primary,
                        },
                        new NativeCommand
                        {
                            Tick = 0U,
                            ActorId = second,
                            Ability = NativeAbility.Primary,
                        },
                    });

                    NativeWorldStats stats = world.GetStats();
                    Assert(stats.Tick == 1U, "tick");
                    Assert(stats.ActorCount == 2U, "actor count");
                    Assert(stats.AliveCount == 0U, "simultaneous deaths");
                    Assert(stats.AttacksResolved == 2U, "resolved attacks");
                    Assert(!world.GetActor(first).IsAlive, "actor state marshalling");
                }

                Console.WriteLine(
                    "[PASS] interop/csharp-pinvoke-native-roundtrip "
                    + "(SafeHandle create/spawn/step/query/dispose)");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("[FAIL] interop: " + exception);
                return 1;
            }
        }

        private static NativeActorSpawn CreateSpawn(uint team, int x)
        {
            return new NativeActorSpawn
            {
                Team = team,
                PositionXMm = x,
                PositionYMm = 0,
                Health = 10,
                SpeedMmPerTick = 0,
                Damage = 10,
                AttackRangeMm = 1_000,
                AttackCooldownTicks = 1U,
            };
        }

        private static string ParseLibraryPath(string[] args)
        {
            if (args.Length != 2 || args[0] != "--library")
            {
                throw new ArgumentException(
                    "Usage: --library ABSOLUTE_PATH_TO_NATIVE_LIBRARY");
            }
            string path = Path.GetFullPath(args[1]);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    "Native library was not found.",
                    path);
            }
            return path;
        }

        private static void Assert(bool condition, string label)
        {
            if (!condition)
            {
                throw new InvalidOperationException(
                    "Assertion failed: " + label + ".");
            }
        }
    }
}
