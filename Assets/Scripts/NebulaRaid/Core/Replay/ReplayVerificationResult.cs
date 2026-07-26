namespace NebulaRaid.Replay
{
    public readonly struct ReplayVerificationResult
    {
        private ReplayVerificationResult(
            bool isValid,
            int checkedFrames,
            int mismatchTick,
            ulong expectedChecksum,
            ulong actualChecksum,
            string message)
        {
            IsValid = isValid;
            CheckedFrames = checkedFrames;
            MismatchTick = mismatchTick;
            ExpectedChecksum = expectedChecksum;
            ActualChecksum = actualChecksum;
            Message = message;
        }

        public bool IsValid { get; }
        public int CheckedFrames { get; }
        public int MismatchTick { get; }
        public ulong ExpectedChecksum { get; }
        public ulong ActualChecksum { get; }
        public string Message { get; }

        public static ReplayVerificationResult Success(int checkedFrames)
        {
            return new ReplayVerificationResult(
                true,
                checkedFrames,
                -1,
                0,
                0,
                "Replay checksums match.");
        }

        public static ReplayVerificationResult Mismatch(
            int checkedFrames,
            int tick,
            ulong expected,
            ulong actual)
        {
            return new ReplayVerificationResult(
                false,
                checkedFrames,
                tick,
                expected,
                actual,
                "Checksum mismatch at tick " + tick + ".");
        }
    }
}

