namespace InstantAIGate.Server.Configuration;

using System.Collections.Generic;

public sealed record StartupModelItem
{
    public string RepoId { get; init; } = string.Empty;
    public string Profile { get; init; } = string.Empty;
}

public sealed record StartupModelSettings
{
    public bool Enabled { get; init; } = false;
    public string[] RepoId { get; init; } = [];
    public string Profile { get; init; } = "Default";
    public List<StartupModelItem> Models { get; init; } = [];
}

