namespace NebulaRaid.HotUpdate
{
    public readonly struct HotUpdateActivationResult
    {
        private HotUpdateActivationResult(
            bool succeeded,
            string activeVersion,
            string previousVersion,
            string message)
        {
            Succeeded = succeeded;
            ActiveVersion = activeVersion;
            PreviousVersion = previousVersion;
            Message = message;
        }

        public bool Succeeded { get; }
        public string ActiveVersion { get; }
        public string PreviousVersion { get; }
        public string Message { get; }

        public static HotUpdateActivationResult Success(
            string activeVersion,
            string previousVersion,
            string message)
        {
            return new HotUpdateActivationResult(
                true,
                activeVersion,
                previousVersion,
                message);
        }

        public static HotUpdateActivationResult Failure(
            string activeVersion,
            string message)
        {
            return new HotUpdateActivationResult(false, activeVersion, activeVersion, message);
        }
    }
}

