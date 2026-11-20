namespace InstantAIGate.Client.Chat;

/// <summary>
/// Immutable progress snapshot of a checkpointed Map-Reduce pipeline, delivered through
/// <see cref="System.IProgress{T}"/> so consumers can map it onto any UI or logging surface
/// without the SDK taking a dependency on application-specific reporters.
/// </summary>
/// <param name="Phase">The pipeline stage currently in progress.</param>
/// <param name="Completed">The number of completed units within the current stage.</param>
/// <param name="Total">The total number of units within the current stage.</param>
/// <param name="Message">A human-readable, English status message suitable for direct display.</param>
public sealed record MapReduceProgress(
    MapReducePhase Phase,
    int Completed,
    int Total,
    string Message);
