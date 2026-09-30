namespace InstantAIGate.Core.Dtos.Session;

using InstantAIGate.Core.Dtos.Config;

public sealed record SessionStartRequest(
    string SessionId,
    string RepoId,
    InferenceSettings? Settings = null
);