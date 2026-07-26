namespace NebulaRaid.HotUpdate
{
    public sealed class PackageVerificationResult
    {
        private PackageVerificationResult(
            bool isValid,
            string packageDirectory,
            HotUpdateManifest? manifest,
            string message)
        {
            IsValid = isValid;
            PackageDirectory = packageDirectory;
            Manifest = manifest;
            Message = message;
        }

        public bool IsValid { get; }
        public string PackageDirectory { get; }
        public HotUpdateManifest? Manifest { get; }
        public string Message { get; }

        public static PackageVerificationResult Valid(
            string packageDirectory,
            HotUpdateManifest manifest)
        {
            return new PackageVerificationResult(
                true,
                packageDirectory,
                manifest,
                "Manifest and all file hashes are valid.");
        }

        public static PackageVerificationResult Invalid(string packageDirectory, string message)
        {
            return new PackageVerificationResult(false, packageDirectory, null, message);
        }
    }
}

