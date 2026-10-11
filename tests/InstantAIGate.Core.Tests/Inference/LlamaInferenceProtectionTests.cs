namespace InstantAIGate.Core.Tests.Inference;

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
            int contextLimit = activeConfig!.ContextSize;
            sessionManager.UpdatePastTokensCount(sessionId, contextLimit - 10);

            var deltaMessage = new ChatMessage(
                "user",
                "This is a long test prompt that will definitively exceed the remaining context limit and trigger the Fail-Safe Guard.");

            var enumerator = inferenceEngine.StreamDeltaGenerationAsync(sessionId, deltaMessage);

            var act = async () =>
            {
                await foreach (var _ in enumerator) { }
            };

            var ex = await Assert.ThrowsAsync<ContextOverflowException>(act);
            Assert.Equal(sessionId, ex.SessionId);
        }
        finally
        {
            await sessionManager.ReleaseSessionAsync(sessionId);
        }
    }
}