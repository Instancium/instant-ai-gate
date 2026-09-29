namespace InstantAIGate.Core.Tests.TestConfiguration;

/// <summary>
/// Typed parameters of the test model.
/// Mapped from the "InstantAIGate:TestData" section of the test project appsettings.json.
/// </summary>
public sealed class TestModelOptions
{
    public const string SectionName = "InstantAIGate:TestData";

    /// <summary>Default target model for integration tests (lighter qwen3-vl-2b-instruct).</summary>
    public string RepoId { get; init; } = "qwen3-vl-2b-instruct";

    /// <summary>Relative GGUF file name inside the model repository directory (integrity validator).</summary>
    public string GgufFileName { get; init; } = "Qwen3VL-2B-Instruct-Q4_K_M.gguf";

    /// <summary>Directory containing models on the test stand.</summary>
    public string ModelsDirectory { get; init; } = @"C:\models";

    /// <summary>Model load parameters (inference tests).</summary>
    public TestInferenceLoadOptions ModelLoad { get; init; } = new();

    /// <summary>Inference request parameters (E2E / vision tests).</summary>
    public TestInferenceRequestOptions Inference { get; init; } = new();

    /// <summary>Synthetic download stubs and timeouts for downloader tests.</summary>
    public TestSyntheticDownloadOptions SyntheticDownload { get; init; } = new();
}

/// <summary>
/// Model initialization parameters in tests (test-stand analog of HardwareProfile).
/// </summary>
public sealed class TestInferenceLoadOptions
{
    public int GpuLayerCount { get; init; } = 99;
    public int ContextSize { get; init; } = 4096;
    public int BatchSize { get; init; } = 512;
    public int Threads { get; init; } = 8;
    public bool VisionSupport { get; init; } = true;
}

/// <summary>
/// Inference invocation parameters in tests and the expected result.
/// </summary>
public sealed class TestInferenceRequestOptions
{
    public int MaxTokens { get; init; } = 200;
    public float Temperature { get; init; } = 0.5f;
    public int E2eMaxTokens { get; init; } = 10;
    public float E2eTemperature { get; init; } = 0.1f;
    public int Seed { get; init; } = 42;

    /// <summary>Prompt for the vision test.</summary>
    public string VisionPrompt { get; init; } =
        "Please extract the main headline printed in large letters on this newspaper.";

    /// <summary>Prompt for the gateway E2E test.</summary>
    public string E2ePrompt { get; init; } = "Respond with exactly one word: 'Acknowledged'.";

    /// <summary>Expected keywords/fragments in the model answer (substring check, case-insensitive).</summary>
    public List<string> ExpectedKeywords { get; init; } = new() { "MEN WALK ON MOON" };
}

/// <summary>
/// Synthetic sizes and URLs used by parallel downloader / SSR pipeline tests.
/// </summary>
public sealed class TestSyntheticDownloadOptions
{
    /// <summary>Base URL of synthetic download endpoints.</summary>
    public string BaseUrl { get; init; } = "http://synth/";

    /// <summary>Synthetic file size for parallel download tests (50 MB).</summary>
    public long ParallelDownloadFileSizeBytes { get; init; } = 52_428_800;

    /// <summary>Synthetic file size for sequential fallback tests (20 MB).</summary>
    public long SequentialFallbackFileSizeBytes { get; init; } = 20_971_520;

    /// <summary>Synthetic file size for cancellation tests (5 GB).</summary>
    public long CancellationTestFileSizeBytes { get; init; } = 5_368_709_120;

    /// <summary>Synthetic file size for corrupted payload tests (5 MB).</summary>
    public long CorruptedPayloadFileSizeBytes { get; init; } = 5_242_880;
}
