using System;
using System.IO;

namespace NebulaRaid.HotUpdate
{
    /// <summary>
    /// Copies verified packages into immutable version folders and switches a tiny
    /// active-version pointer only after the runtime adapter accepts the entrypoint.
    /// </summary>
    public sealed class HotUpdateReleaseStore
    {
        private const string ActivePointer = "active.version";
        private const string LastKnownGoodPointer = "last-known-good.version";
        private readonly string _root;
        private readonly HotUpdatePackageVerifier _verifier;
        private readonly ILuaRuntimeProbe _runtimeProbe;

        public HotUpdateReleaseStore(
            string installRoot,
            HotUpdatePackageVerifier verifier,
            ILuaRuntimeProbe runtimeProbe)
        {
            _root = Path.GetFullPath(
                installRoot ?? throw new ArgumentNullException(nameof(installRoot)));
            _verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
            _runtimeProbe = runtimeProbe ?? throw new ArgumentNullException(nameof(runtimeProbe));
        }

        public string GetActiveVersion()
        {
            return ReadPointer(ActivePointer);
        }

        public HotUpdateActivationResult InstallAndActivate(string packageDirectory)
        {
            PackageVerificationResult source = _verifier.Verify(packageDirectory);
            string previousVersion = GetActiveVersion();
            if (!source.IsValid || source.Manifest == null)
            {
                return HotUpdateActivationResult.Failure(
                    previousVersion,
                    "Package rejected: " + source.Message);
            }

            HotUpdateManifest manifest = source.Manifest;
            string releasesRoot = Path.Combine(_root, "releases");
            string finalRelease = Path.Combine(releasesRoot, manifest.BundleVersion);
            string staging = finalRelease + ".staging-" + Guid.NewGuid().ToString("N");
            try
            {
                Directory.CreateDirectory(releasesRoot);
                if (!Directory.Exists(finalRelease))
                {
                    CopyVerifiedPackage(source.PackageDirectory, staging, manifest);
                    PackageVerificationResult staged = _verifier.Verify(staging);
                    if (!staged.IsValid
                        || staged.Manifest == null
                        || !ManifestsMatch(manifest, staged.Manifest))
                    {
                        return HotUpdateActivationResult.Failure(
                            previousVersion,
                            "Staging verification failed or content changed: " + staged.Message);
                    }

                    Directory.Move(staging, finalRelease);
                }
                else
                {
                    PackageVerificationResult existing = _verifier.Verify(finalRelease);
                    if (!existing.IsValid
                        || existing.Manifest == null
                        || !ManifestsMatch(manifest, existing.Manifest))
                    {
                        return HotUpdateActivationResult.Failure(
                            previousVersion,
                            "Existing release is invalid or version content differs: "
                            + existing.Message);
                    }
                }

                if (!_runtimeProbe.CanLoad(
                    finalRelease,
                    manifest.Entrypoint,
                    out string failureReason))
                {
                    return HotUpdateActivationResult.Failure(
                        previousVersion,
                        "Runtime probe rejected the release: " + failureReason);
                }

                if (previousVersion.Length > 0 && previousVersion != manifest.BundleVersion)
                {
                    AtomicWritePointer(LastKnownGoodPointer, previousVersion);
                }

                AtomicWritePointer(ActivePointer, manifest.BundleVersion);
                return HotUpdateActivationResult.Success(
                    manifest.BundleVersion,
                    previousVersion,
                    "Release verified, probed and activated.");
            }
            catch (IOException exception)
            {
                return HotUpdateActivationResult.Failure(
                    previousVersion,
                    "Activation I/O failure: " + exception.Message);
            }
            catch (UnauthorizedAccessException exception)
            {
                return HotUpdateActivationResult.Failure(
                    previousVersion,
                    "Activation access failure: " + exception.Message);
            }
            finally
            {
                if (Directory.Exists(staging))
                {
                    Directory.Delete(staging, true);
                }
            }
        }

        public HotUpdateActivationResult Rollback()
        {
            string current = GetActiveVersion();
            string target = ReadPointer(LastKnownGoodPointer);
            if (target.Length == 0)
            {
                return HotUpdateActivationResult.Failure(current, "No last-known-good release exists.");
            }

            try
            {
                SemanticVersion.Parse(target, "last-known-good version");
                string targetDirectory = Path.Combine(_root, "releases", target);
                PackageVerificationResult verified = _verifier.Verify(targetDirectory);
                if (!verified.IsValid || verified.Manifest == null)
                {
                    return HotUpdateActivationResult.Failure(
                        current,
                        "Rollback release failed verification: " + verified.Message);
                }

                if (!_runtimeProbe.CanLoad(
                    targetDirectory,
                    verified.Manifest.Entrypoint,
                    out string failureReason))
                {
                    return HotUpdateActivationResult.Failure(
                        current,
                        "Rollback runtime probe failed: " + failureReason);
                }

                AtomicWritePointer(ActivePointer, target);
                return HotUpdateActivationResult.Success(target, current, "Rolled back safely.");
            }
            catch (FormatException exception)
            {
                return HotUpdateActivationResult.Failure(current, exception.Message);
            }
            catch (IOException exception)
            {
                return HotUpdateActivationResult.Failure(
                    current,
                    "Rollback I/O failure: " + exception.Message);
            }
            catch (UnauthorizedAccessException exception)
            {
                return HotUpdateActivationResult.Failure(
                    current,
                    "Rollback access failure: " + exception.Message);
            }
        }

        private static void CopyVerifiedPackage(
            string sourceRoot,
            string destinationRoot,
            HotUpdateManifest manifest)
        {
            Directory.CreateDirectory(destinationRoot);
            File.Copy(
                Path.Combine(sourceRoot, "manifest.json"),
                Path.Combine(destinationRoot, "manifest.json"),
                false);
            for (int i = 0; i < manifest.Files.Length; i++)
            {
                HotUpdateFile file = manifest.Files[i];
                string source = HotUpdatePackageVerifier.ResolveSafePath(sourceRoot, file.Path);
                string destination = HotUpdatePackageVerifier.ResolveSafePath(
                    destinationRoot,
                    file.Path);
                string? parent = Path.GetDirectoryName(destination);
                if (parent != null)
                {
                    Directory.CreateDirectory(parent);
                }

                File.Copy(source, destination, false);
            }
        }

        private string ReadPointer(string fileName)
        {
            string path = Path.Combine(_root, fileName);
            if (!File.Exists(path))
            {
                return string.Empty;
            }

            string value = File.ReadAllText(path).Trim();
            if (value.Length == 0)
            {
                return string.Empty;
            }

            try
            {
                SemanticVersion.Parse(value, fileName);
                return value;
            }
            catch (FormatException)
            {
                return string.Empty;
            }
        }

        private static bool ManifestsMatch(HotUpdateManifest expected, HotUpdateManifest actual)
        {
            if (expected.SchemaVersion != actual.SchemaVersion
                || expected.BundleVersion != actual.BundleVersion
                || expected.MinimumAppVersion != actual.MinimumAppVersion
                || expected.Entrypoint != actual.Entrypoint
                || expected.Files.Length != actual.Files.Length)
            {
                return false;
            }

            for (int i = 0; i < expected.Files.Length; i++)
            {
                HotUpdateFile left = expected.Files[i];
                HotUpdateFile right = actual.Files[i];
                if (left.Path != right.Path
                    || !string.Equals(left.Sha256, right.Sha256, StringComparison.OrdinalIgnoreCase)
                    || left.Size != right.Size)
                {
                    return false;
                }
            }

            return true;
        }

        private void AtomicWritePointer(string fileName, string version)
        {
            SemanticVersion.Parse(version, fileName);
            Directory.CreateDirectory(_root);
            string destination = Path.Combine(_root, fileName);
            string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temporary, version + Environment.NewLine);
            try
            {
                if (File.Exists(destination))
                {
                    File.Replace(temporary, destination, null);
                }
                else
                {
                    File.Move(temporary, destination);
                }
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }
    }
}
