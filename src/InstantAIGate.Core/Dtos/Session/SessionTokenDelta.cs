namespace InstantAIGate.Core.Dtos.Session;

public sealed record SessionTokenDelta(
    string SessionId,
    string Content,
    bool IsDone = false,
    string? FinishReason = null
);