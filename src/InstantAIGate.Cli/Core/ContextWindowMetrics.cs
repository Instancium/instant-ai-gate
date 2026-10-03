namespace InstantAIGate.Cli.Core;

/// <summary>
/// Immutable snapshot describing the utilization of the model's context window for an active session.
/// </summary>
public sealed record ContextWindowMetrics
{
    public required string SessionId { get; init; }
    public required string RepoId { get; init; }
    public required int ContextCapacity { get; init; }
    public required int PastTokensCount { get; init; }
    public required int SystemPrefixTokensCount { get; init; }

    public int HistoryTokensCount => Math.Max(0, PastTokensCount - SystemPrefixTokensCount);
    public int AvailableTokensReserve => Math.Max(0, ContextCapacity - PastTokensCount);
    public double UtilizationPercentage => ContextCapacity > 0
        ? Math.Min(100.0, Math.Round((double)PastTokensCount / ContextCapacity * 100.0, 2))
        : 0.0;

    public bool IsNearCapacity(double thresholdPercentage = 85.0) =>
        UtilizationPercentage >= thresholdPercentage;
}