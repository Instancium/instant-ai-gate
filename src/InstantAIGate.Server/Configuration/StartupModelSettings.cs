namespace InstantAIGate.Server.Configuration
{
    public sealed record StartupModelSettings
    {
        public bool AutoLoad { get; init; } = false;
        public string RepoId { get; init; } = string.Empty;
        public string Profile { get; init; } = "Default";
    }
}
