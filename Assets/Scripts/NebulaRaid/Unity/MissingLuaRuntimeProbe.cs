using NebulaRaid.HotUpdate;

namespace NebulaRaid.Unity
{
    /// <summary>
    /// Fail-closed placeholder. Replace it with a project-specific adapter only
    /// after importing and configuring a real Lua runtime.
    /// </summary>
    public sealed class MissingLuaRuntimeProbe : ILuaRuntimeProbe
    {
        public bool CanLoad(
            string releaseDirectory,
            string entrypointRelativePath,
            out string failureReason)
        {
            failureReason =
                "No Lua VM is bundled. Install and audit a runtime adapter before activation.";
            return false;
        }
    }
}

