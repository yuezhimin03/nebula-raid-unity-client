using System;

namespace NebulaRaid.HotUpdate
{
    public sealed class HotUpdatePolicy
    {
        public HotUpdatePolicy(
            string currentAppVersion,
            int maximumFiles = 256,
            long maximumFileBytes = 2 * 1024 * 1024,
            long maximumBundleBytes = 16 * 1024 * 1024)
        {
            if (maximumFiles <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumFiles));
            }

            if (maximumFileBytes <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumFileBytes));
            }

            if (maximumBundleBytes <= 0 || maximumBundleBytes < maximumFileBytes)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumBundleBytes));
            }

            CurrentAppVersion = currentAppVersion
                ?? throw new ArgumentNullException(nameof(currentAppVersion));
            ParsedCurrentAppVersion = SemanticVersion.Parse(currentAppVersion, "currentAppVersion");
            MaximumFiles = maximumFiles;
            MaximumFileBytes = maximumFileBytes;
            MaximumBundleBytes = maximumBundleBytes;
        }

        public string CurrentAppVersion { get; }
        public int MaximumFiles { get; }
        public long MaximumFileBytes { get; }
        public long MaximumBundleBytes { get; }
        internal SemanticVersion ParsedCurrentAppVersion { get; }
    }
}

