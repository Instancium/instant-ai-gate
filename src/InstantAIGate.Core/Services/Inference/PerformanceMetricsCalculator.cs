namespace InstantAIGate.Core.Services.Inference;

using InstantAIGate.Core.Dtos.Inference.Native;

/// <summary>
/// Pure, side-effect-free calculator for inference throughput metrics (tokens per second).
/// Isolated from the native layer so it can be unit tested without loading llama.cpp.
/// </summary>
public static class PerformanceMetricsCalculator
{
    private const double MillisecondsPerSecond = 1000.0;

    /// <summary>
    /// Computes tokens-per-second. Returns 0.0 for any non-physical input
    /// (zero/negative counts, zero/negative/NaN/Infinity elapsed time) to guard
    /// against divide-by-zero on cold contexts where the native counters are empty.
    /// </summary>
    public static double ComputeTps(long tokenCount, double elapsedMs)
    {
        if (tokenCount <= 0 || elapsedMs <= 0.0 || double.IsNaN(elapsedMs) || double.IsInfinity(elapsedMs))
        {
            return 0.0;
        }

        return tokenCount / elapsedMs * MillisecondsPerSecond;
    }

    public static double ComputePromptTps(ContextPerformanceData data) =>
        ComputeTps(data.PromptTokens, data.PromptMs);

    public static double ComputeEvalTps(ContextPerformanceData data) =>
        ComputeTps(data.EvalTokens, data.EvalMs);
}
