using System;
using System.Runtime.InteropServices;

namespace NebulaRaid.Unity.Native
{
    public enum NativeStatus
    {
        Ok = 0,
        InvalidArgument = 1,
        CapacityExceeded = 2,
        NoncanonicalCommands = 3,
        OutOfMemory = 4,
        InternalError = 5,
    }

    [Flags]
    public enum NativeAbility : uint
    {
        None = 0,
        Primary = 1,
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NativeWorldConfig
    {
        public uint Capacity;
        public int ArenaHalfExtentMm;
        public int GridCellSizeMm;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NativeActorSpawn
    {
        public uint Team;
        public int PositionXMm;
        public int PositionYMm;
        public int Health;
        public int SpeedMmPerTick;
        public int Damage;
        public int AttackRangeMm;
        public uint AttackCooldownTicks;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NativeCommand
    {
        public uint Tick;
        public uint ActorId;
        public int MoveX;
        public int MoveY;
        public NativeAbility Ability;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NativeActorState
    {
        public uint ActorId;
        public uint Team;
        public int PositionXMm;
        public int PositionYMm;
        public int Health;
        public uint Alive;
        public uint CooldownTicks;

        public bool IsAlive
        {
            get { return Alive != 0; }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NativeWorldStats
    {
        public uint Tick;
        public uint ActorCount;
        public uint AliveCount;
        public uint Reserved;
        public ulong AttacksResolved;
        public ulong Checksum;
    }

    /// <summary>
    /// Ownership-safe C# facade over the C++17 simulation C ABI.
    /// The native DLL is optional: projects that do not deploy it can keep using
    /// the pure C# simulation without touching this type.
    /// </summary>
    public sealed class NativeSimulationWorld : IDisposable
    {
        private NativeWorldHandle? handle;

        public NativeSimulationWorld(NativeWorldConfig config)
        {
            NativeStatus status = NativeMethods.WorldCreate(
                ref config,
                out IntPtr pointer);
            ThrowIfFailed(status, "nebula_world_create");
            if (pointer == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    "nebula_world_create returned a null world.");
            }

            handle = new NativeWorldHandle(pointer);
        }

        public static uint AbiVersion
        {
            get { return NativeMethods.AbiVersion(); }
        }

        public uint Spawn(NativeActorSpawn spawn)
        {
            NativeStatus status = NativeMethods.WorldSpawn(
                GetHandle(),
                ref spawn,
                out uint actorId);
            ThrowIfFailed(status, "nebula_world_spawn");
            return actorId;
        }

        public void Step(NativeCommand[] commands)
        {
            if (commands == null)
            {
                throw new ArgumentNullException(nameof(commands));
            }

            NativeStatus status = NativeMethods.WorldStep(
                GetHandle(),
                commands,
                new UIntPtr((uint)commands.Length));
            ThrowIfFailed(status, "nebula_world_step");
        }

        public NativeActorState GetActor(uint actorId)
        {
            NativeStatus status = NativeMethods.WorldGetActor(
                GetHandle(),
                actorId,
                out NativeActorState state);
            ThrowIfFailed(status, "nebula_world_get_actor");
            return state;
        }

        public NativeWorldStats GetStats()
        {
            NativeStatus status = NativeMethods.WorldGetStats(
                GetHandle(),
                out NativeWorldStats stats);
            ThrowIfFailed(status, "nebula_world_get_stats");
            return stats;
        }

        public void Dispose()
        {
            NativeWorldHandle? owned = handle;
            handle = null;
            if (owned != null)
            {
                owned.Dispose();
            }
            GC.SuppressFinalize(this);
        }

        private NativeWorldHandle GetHandle()
        {
            NativeWorldHandle? current = handle;
            if (current == null || current.IsClosed || current.IsInvalid)
            {
                throw new ObjectDisposedException(nameof(NativeSimulationWorld));
            }
            return current;
        }

        private static void ThrowIfFailed(NativeStatus status, string operation)
        {
            if (status != NativeStatus.Ok)
            {
                throw new NativeSimulationException(operation, status);
            }
        }
    }

    public sealed class NativeSimulationException : InvalidOperationException
    {
        public NativeSimulationException(string operation, NativeStatus status)
            : base(operation + " failed with native status " + status + ".")
        {
            Status = status;
        }

        public NativeStatus Status { get; }
    }

    internal sealed class NativeWorldHandle : SafeHandle
    {
        internal NativeWorldHandle(IntPtr pointer)
            : base(IntPtr.Zero, true)
        {
            SetHandle(pointer);
        }

        public override bool IsInvalid
        {
            get { return handle == IntPtr.Zero || handle == new IntPtr(-1); }
        }

        protected override bool ReleaseHandle()
        {
            NativeMethods.WorldDestroy(handle);
            return true;
        }
    }

    internal static class NativeMethods
    {
#if UNITY_IOS && !UNITY_EDITOR
        private const string LibraryName = "__Internal";
#else
        private const string LibraryName = "NebulaNative";
#endif

        [DllImport(
            LibraryName,
            EntryPoint = "nebula_native_abi_version",
            CallingConvention = CallingConvention.Cdecl)]
        internal static extern uint AbiVersion();

        [DllImport(
            LibraryName,
            EntryPoint = "nebula_world_create",
            CallingConvention = CallingConvention.Cdecl)]
        internal static extern NativeStatus WorldCreate(
            ref NativeWorldConfig config,
            out IntPtr world);

        [DllImport(
            LibraryName,
            EntryPoint = "nebula_world_destroy",
            CallingConvention = CallingConvention.Cdecl)]
        internal static extern void WorldDestroy(IntPtr world);

        [DllImport(
            LibraryName,
            EntryPoint = "nebula_world_spawn",
            CallingConvention = CallingConvention.Cdecl)]
        internal static extern NativeStatus WorldSpawn(
            NativeWorldHandle world,
            ref NativeActorSpawn spawn,
            out uint actorId);

        [DllImport(
            LibraryName,
            EntryPoint = "nebula_world_step",
            CallingConvention = CallingConvention.Cdecl)]
        internal static extern NativeStatus WorldStep(
            NativeWorldHandle world,
            [In] NativeCommand[] commands,
            UIntPtr commandCount);

        [DllImport(
            LibraryName,
            EntryPoint = "nebula_world_get_actor",
            CallingConvention = CallingConvention.Cdecl)]
        internal static extern NativeStatus WorldGetActor(
            NativeWorldHandle world,
            uint actorId,
            out NativeActorState state);

        [DllImport(
            LibraryName,
            EntryPoint = "nebula_world_get_stats",
            CallingConvention = CallingConvention.Cdecl)]
        internal static extern NativeStatus WorldGetStats(
            NativeWorldHandle world,
            out NativeWorldStats stats);
    }
}
