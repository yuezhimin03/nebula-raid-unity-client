using System;

namespace NebulaRaid.HotUpdate
{
    public readonly struct HotUpdateFile
    {
        public HotUpdateFile(string path, string sha256, long size)
        {
            Path = path ?? throw new ArgumentNullException(nameof(path));
            Sha256 = sha256 ?? throw new ArgumentNullException(nameof(sha256));
            Size = size;
        }

        public string Path { get; }
        public string Sha256 { get; }
        public long Size { get; }
    }

    public sealed class HotUpdateManifest
    {
        public HotUpdateManifest(
            int schemaVersion,
            string bundleVersion,
            string minimumAppVersion,
            string entrypoint,
            HotUpdateFile[] files)
        {
            SchemaVersion = schemaVersion;
            BundleVersion = bundleVersion ?? throw new ArgumentNullException(nameof(bundleVersion));
            MinimumAppVersion = minimumAppVersion
                ?? throw new ArgumentNullException(nameof(minimumAppVersion));
            Entrypoint = entrypoint ?? throw new ArgumentNullException(nameof(entrypoint));
            Files = files == null
                ? throw new ArgumentNullException(nameof(files))
                : (HotUpdateFile[])files.Clone();
        }

        public int SchemaVersion { get; }
        public string BundleVersion { get; }
        public string MinimumAppVersion { get; }
        public string Entrypoint { get; }
        public HotUpdateFile[] Files { get; }
    }
}

