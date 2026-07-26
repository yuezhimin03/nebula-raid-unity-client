using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace NebulaRaid.HotUpdate
{
    public sealed class HotUpdatePackageVerifier
    {
        private const int MaximumManifestBytes = 1024 * 1024;
        private readonly HotUpdatePolicy _policy;

        public HotUpdatePackageVerifier(HotUpdatePolicy policy)
        {
            _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        }

        public PackageVerificationResult Verify(string packageDirectory)
        {
            if (packageDirectory == null)
            {
                return PackageVerificationResult.Invalid(
                    string.Empty,
                    "Package directory is required.");
            }

            string fullRoot;
            try
            {
                fullRoot = Path.GetFullPath(packageDirectory);
                if (!Directory.Exists(fullRoot))
                {
                    return PackageVerificationResult.Invalid(fullRoot, "Package directory does not exist.");
                }

                string manifestPath = Path.Combine(fullRoot, "manifest.json");
                FileInfo manifestInfo = new FileInfo(manifestPath);
                if (!manifestInfo.Exists)
                {
                    return PackageVerificationResult.Invalid(fullRoot, "manifest.json is missing.");
                }

                if (manifestInfo.Length <= 0 || manifestInfo.Length > MaximumManifestBytes)
                {
                    return PackageVerificationResult.Invalid(
                        fullRoot,
                        "manifest.json is empty or exceeds the size limit.");
                }

                HotUpdateManifest manifest = HotUpdateManifestCodec.Parse(
                    File.ReadAllText(manifestPath));
                ValidateManifestMetadata(manifest);
                ValidateFiles(fullRoot, manifest);
                return PackageVerificationResult.Valid(fullRoot, manifest);
            }
            catch (FormatException exception)
            {
                return PackageVerificationResult.Invalid(
                    packageDirectory ?? string.Empty,
                    exception.Message);
            }
            catch (IOException exception)
            {
                return PackageVerificationResult.Invalid(
                    packageDirectory ?? string.Empty,
                    "I/O validation failure: " + exception.Message);
            }
            catch (UnauthorizedAccessException exception)
            {
                return PackageVerificationResult.Invalid(
                    packageDirectory ?? string.Empty,
                    "Access denied during validation: " + exception.Message);
            }
            catch (CryptographicException exception)
            {
                return PackageVerificationResult.Invalid(
                    packageDirectory ?? string.Empty,
                    "Hash validation failure: " + exception.Message);
            }
        }

        private void ValidateManifestMetadata(HotUpdateManifest manifest)
        {
            if (manifest.SchemaVersion != 1)
            {
                throw new FormatException("Unsupported manifest schema version.");
            }

            SemanticVersion.Parse(manifest.BundleVersion, "bundleVersion");
            SemanticVersion minimum = SemanticVersion.Parse(
                manifest.MinimumAppVersion,
                "minimumAppVersion");
            if (minimum.CompareTo(_policy.ParsedCurrentAppVersion) > 0)
            {
                throw new FormatException("Bundle requires a newer app version.");
            }

            if (manifest.Files.Length == 0 || manifest.Files.Length > _policy.MaximumFiles)
            {
                throw new FormatException("Manifest file count is outside policy.");
            }
        }

        private void ValidateFiles(string root, HotUpdateManifest manifest)
        {
            HashSet<string> paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool foundEntrypoint = false;
            long totalBytes = 0;
            for (int i = 0; i < manifest.Files.Length; i++)
            {
                HotUpdateFile file = manifest.Files[i];
                ValidateRelativeLuaPath(file.Path);
                if (!paths.Add(file.Path))
                {
                    throw new FormatException("Duplicate file path: " + file.Path + ".");
                }

                if (file.Path == manifest.Entrypoint)
                {
                    foundEntrypoint = true;
                }

                if (file.Size < 0 || file.Size > _policy.MaximumFileBytes)
                {
                    throw new FormatException("File size is outside policy: " + file.Path + ".");
                }

                if (totalBytes > _policy.MaximumBundleBytes - file.Size)
                {
                    throw new FormatException("Bundle exceeds the total size policy.");
                }

                totalBytes += file.Size;
                string fullPath = ResolveSafePath(root, file.Path);
                RejectReparsePoints(root, file.Path);
                FileInfo info = new FileInfo(fullPath);
                if (!info.Exists)
                {
                    throw new FormatException("Manifest file is missing: " + file.Path + ".");
                }

                if (info.Length != file.Size)
                {
                    throw new FormatException("Size mismatch: " + file.Path + ".");
                }

                byte[] expected = ParseSha256(file.Sha256);
                byte[] actual;
                using (FileStream stream = File.OpenRead(fullPath))
                using (SHA256 sha256 = SHA256.Create())
                {
                    actual = sha256.ComputeHash(stream);
                }

                if (!ConstantTimeEquals(expected, actual))
                {
                    throw new FormatException("SHA-256 mismatch: " + file.Path + ".");
                }
            }

            ValidateRelativeLuaPath(manifest.Entrypoint);
            if (!foundEntrypoint)
            {
                throw new FormatException("Entrypoint must be included in files.");
            }
        }

        internal static string ResolveSafePath(string root, string relativePath)
        {
            string normalizedRoot = Path.GetFullPath(root);
            string rootWithSeparator = normalizedRoot.EndsWith(
                Path.DirectorySeparatorChar.ToString(),
                StringComparison.Ordinal)
                ? normalizedRoot
                : normalizedRoot + Path.DirectorySeparatorChar;
            string combined = Path.GetFullPath(
                Path.Combine(normalizedRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!combined.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            {
                throw new FormatException("Path escapes package root: " + relativePath + ".");
            }

            return combined;
        }

        internal static void ValidateRelativeLuaPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)
                || Path.IsPathRooted(path)
                || path.IndexOf('\\') >= 0
                || path.IndexOf(':') >= 0
                || !path.EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
            {
                throw new FormatException("Unsafe or unsupported Lua path: " + path + ".");
            }

            string[] segments = path.Split('/');
            for (int i = 0; i < segments.Length; i++)
            {
                if (segments[i].Length == 0 || segments[i] == "." || segments[i] == "..")
                {
                    throw new FormatException("Unsafe Lua path segment: " + path + ".");
                }
            }
        }

        private static byte[] ParseSha256(string value)
        {
            if (value.Length != 64)
            {
                throw new FormatException("SHA-256 value must contain 64 hex characters.");
            }

            byte[] result = new byte[32];
            for (int i = 0; i < result.Length; i++)
            {
                int high = ParseHex(value[i * 2]);
                int low = ParseHex(value[(i * 2) + 1]);
                if (high < 0 || low < 0)
                {
                    throw new FormatException("SHA-256 value contains non-hex characters.");
                }

                result[i] = (byte)((high << 4) | low);
            }

            return result;
        }

        private static int ParseHex(char value)
        {
            if (value >= '0' && value <= '9')
            {
                return value - '0';
            }

            if (value >= 'a' && value <= 'f')
            {
                return value - 'a' + 10;
            }

            return value >= 'A' && value <= 'F' ? value - 'A' + 10 : -1;
        }

        private static bool ConstantTimeEquals(byte[] expected, byte[] actual)
        {
            if (expected.Length != actual.Length)
            {
                return false;
            }

            int difference = 0;
            for (int i = 0; i < expected.Length; i++)
            {
                difference |= expected[i] ^ actual[i];
            }

            return difference == 0;
        }

        private static void RejectReparsePoints(string root, string relativePath)
        {
            string current = Path.GetFullPath(root);
            string[] segments = relativePath.Split('/');
            for (int i = 0; i < segments.Length; i++)
            {
                current = Path.Combine(current, segments[i]);
                if (File.Exists(current) || Directory.Exists(current))
                {
                    FileAttributes attributes = File.GetAttributes(current);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        throw new FormatException("Symbolic links are not allowed in update packages.");
                    }
                }
            }
        }
    }
}
