namespace InstantAIGate.Core.Dtos.Inference.Native;

/// <summary>
/// Native performance counters for prompt, evaluation, and sampling phases of a model context.
/// </summary>
public record ContextPerformanceData(
    double PromptMs,
    double EvalMs,
    double SampleMs,
    long PromptTokens,
    long EvalTokens,
    long SampleCount);
