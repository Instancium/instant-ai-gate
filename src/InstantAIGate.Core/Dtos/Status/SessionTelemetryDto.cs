namespace InstantAIGate.Core.Dtos.Status;

using System;

/// <summary>
/// Immutable snapshot of a single session's telemetry state for dashboard consumption.
/// </summary>
public sealed record SessionTelemetryDto
{
    public required string SessionId { get; init; }
    public required string RepoId { get; init; }
    public required SessionStateType State { get; init; }
    public int PastTokens { get; init; }
    public int MaxContextTokens { get; init; }
    public double PromptTps { get; init; }
    public double EvalTps { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset LastAccessedAt { get; init; }
    public double IdleSeconds { get; init; }
    public bool HasAllocatedContext { get; init; }
    public string? BoundConnectionId { get; init; }
}
