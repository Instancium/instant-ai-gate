namespace InstantAIGate.Core.Dtos.Session;

using InstantAIGate.Core.Dtos.Config;

/// <summary>
/// Payload to initialize or resume a stateful inference session in the Gateway.
/// </summary>
public sealed record SessionStartRequest(
    string SessionId,
    string RepoId,
    InferenceSettings? Settings = null
);