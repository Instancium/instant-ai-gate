namespace InstantAIGate.Core.Tests.Inference;

using FluentAssertions;
using InstantAIGate.Core.Dtos.Config;
using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Dtos.Session;
using InstantAIGate.Core.Exceptions;
using InstantAIGate.Core.Interfaces.Inference;
using InstantAIGate.Core.Tests.Server;
using InstantAIGate.Core.Tests.TestConfiguration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;
using Xunit;

public class LlamaInferenceProtectionTests : IClassFixture<GatewayTestFixture>
{
    private readonly GatewayTestFixture _fixture;

    public LlamaInferenceProtectionTests(GatewayTestFixture fixture)
    {
        _fixture = fixture;
        // Ensure native libraries are loaded for P/Invoke execution
        InstantAIGate.Native.Bindings.NativeLibraryLoader.Load();
    }

    [Fact]
    public async Task StreamDeltaGenerationAsync_WhenExceedingContextSize_ThrowsContextOverflowException_WithoutDecoding()
    {
        var sessionManager = _fixture.Services.GetRequiredService<ISessionInferenceManager>();
        var inferenceEngine = _fixture.Services.GetRequiredService<IInferenceEngine>();
        var modelManager = _fixture.Services.GetRequiredService<IModelManager>();

        string repoId = TestConfig.Model.RepoId;
        string sessionId = $"overflow-test-{Guid.NewGuid():N}";

        // 1. Load model directly via domain service, bypassing HTTP and Auth Middleware
        var activeConfig = modelManager.GetActiveSettings();
        if (activeConfig == null || activeConfig.RepoId != repoId)
        {
            var loadOptions = TestConfig.Model.ModelLoad;
            var config = new ModelSettings
            {
                RepoId = repoId,
                ContextSize = loadOptions.ContextSize,
                GpuLayerCount = loadOptions.GpuLayerCount,
                BatchSize = loadOptions.BatchSize,
                Threads = loadOptions.Threads,
                VisionSupport = loadOptions.VisionSupport,
                Type = loadOptions.VisionSupport ? ModelType.Vlm : ModelType.Llm
            };

            await modelManager.LoadModelAsync(config);
            activeConfig = modelManager.GetActiveSettings();
        }

        await sessionManager.CreateSessionAsync(new SessionStartRequest(sessionId, repoId));

        try
        {
            // 2. Artificially saturate the session's KV-cache counter to 10 tokens below the absolute limit
            int contextLimit = activeConfig!.ContextSize;
            sessionManager.UpdatePastTokensCount(sessionId, contextLimit - 10);

            // 3. Provide a prompt that will definitively exceed the remaining 10 tokens + required reserve
            var deltaMessage = new ChatMessage("user", "This is a long test prompt that will definitively exceed the remaining context limit and trigger the Fail-Safe Guard.");

            var enumerator = inferenceEngine.StreamDeltaGenerationAsync(sessionId, deltaMessage);

            var act = async () =>
            {
                await foreach (var _ in enumerator) { }
            };

            // 4. Assert that the Fail-Safe Guard triggers a deterministic ContextOverflowException
            await act.Should().ThrowAsync<ContextOverflowException>()
                .Where(ex => ex.SessionId == sessionId);
        }
        finally
        {
            // Clean up session context gracefully
            await sessionManager.ReleaseSessionAsync(sessionId);
        }
    }
}