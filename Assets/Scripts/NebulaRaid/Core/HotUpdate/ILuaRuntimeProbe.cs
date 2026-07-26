namespace NebulaRaid.HotUpdate
{
    /// <summary>
    /// Integration boundary for a real Lua runtime adapter (for example xLua).
    /// This repository intentionally does not claim or bundle a Lua VM.
    /// </summary>
    public interface ILuaRuntimeProbe
    {
        bool CanLoad(
            string releaseDirectory,
            string entrypointRelativePath,
            out string failureReason);
    }
}

