namespace InstantAIGate.Core.Tests.Inference;

using InstantAIGate.Core.Dtos.Config;
using InstantAIGate.Core.Dtos.Inference.Native;
using InstantAIGate.Core.Dtos.Status;
using InstantAIGate.Core.Interfaces.Inference;
using InstantAIGate.Core.Interfaces.Native;
using InstantAIGate.Core.Services.Inference;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

public sealed class BackendPerformanceMetricsTests
{
    [Theory]
    [InlineData(100L, 1000.0, 100.0)]
    [InlineData(50L, 2000.0, 25.0)]
    [InlineData(512L, 4096.0, 125.0)]
    [InlineData(1L, 1000.0, 1.0)]
    public void ComputeTps_ValidInputs_ReturnsExpectedRate(long tokens, double elapsedMs, double expected)
    {
        double actual = PerformanceMetricsCalculator.ComputeTps(tokens, elapsedMs);
        Assert.Equal(expected, actual, precision: 6);
    }

    [Theory]
    [InlineData(0L, 1000.0)]
    [InlineData(100L, 0.0)]
    [InlineData(100L, -5.0)]
    [InlineData(-10L, 1000.0)]
    [InlineData(100L, double.NaN)]
    [InlineData(100L, double.PositiveInfinity)]
    public void ComputeTps_InvalidInputs_ReturnsZero(long tokens, double elapsedMs)
    {
        Assert.Equal(0.0, PerformanceMetricsCalculator.ComputeTps(tokens, elapsedMs));
    }

    [Fact]
    public void ComputePromptTps_FromContextPerformanceData_UsesPromptFieldsOnly()
    {
        var data = new ContextPerformanceData(
            PromptMs: 2000.0,
            EvalMs: 9999.0,
            SampleMs: 10.0,
            PromptTokens: 256,
            EvalTokens: 777,
            SampleCount: 777);

        Assert.Equal(128.0, PerformanceMetricsCalculator.ComputePromptTps(data), precision: 6);
    }

    [Fact]
    public void ComputeEvalTps_FromContextPerformanceData_UsesEvalFieldsOnly()
    {
        var data = new ContextPerformanceData(
            PromptMs: 9999.0,
            EvalMs: 4000.0,
            SampleMs: 10.0,
            PromptTokens: 555,
            EvalTokens: 100,
            SampleCount: 100);

        Assert.Equal(25.0, PerformanceMetricsCalculator.ComputeEvalTps(data), precision: 6);
    }

    [Fact]
    public void Facade_GetContextPerformance_ContractsFlowIntoCalculator()
    {
        var mockFacade = new Mock<IBackendFacade>();
        var mockContextHandle = new Mock<IContextHandle>().Object;
        var perfData = new ContextPerformanceData(
            PromptMs: 1000.0,
            EvalMs: 5000.0,
            SampleMs: 50.0,
            PromptTokens: 120,
            EvalTokens: 200,
            SampleCount: 200);
        mockFacade
            .Setup(f => f.GetContextPerformance(mockContextHandle))
            .Returns(perfData);

        ContextPerformanceData captured = mockFacade.Object.GetContextPerformance(mockContextHandle);

        Assert.Equal(120.0, PerformanceMetricsCalculator.ComputePromptTps(captured), precision: 6);
        Assert.Equal(40.0, PerformanceMetricsCalculator.ComputeEvalTps(captured), precision: 6);
        mockFacade.Verify(f => f.GetContextPerformance(mockContextHandle), Times.Once);
    }

    [Fact]
    public void Facade_GetContextPerformance_ZeroTimings_ProduceZeroTpsWithoutDivisionError()
    {
        var mockFacade = new Mock<IBackendFacade>();
        var mockContextHandle = new Mock<IContextHandle>().Object;
        mockFacade
            .Setup(f => f.GetContextPerformance(mockContextHandle))
            .Returns(new ContextPerformanceData(0, 0, 0, 0, 0, 0));

        ContextPerformanceData captured = mockFacade.Object.GetContextPerformance(mockContextHandle);

        Assert.Equal(0.0, PerformanceMetricsCalculator.ComputePromptTps(captured));
        Assert.Equal(0.0, PerformanceMetricsCalculator.ComputeEvalTps(captured));
    }

    [Fact]
    public void Facade_ResetContextPerformance_IsInvokedBeforePromptPhase()
    {
        var mockFacade = new Mock<IBackendFacade>();
        var mockContextHandle = new Mock<IContextHandle>().Object;

        mockFacade.Object.ResetContextPerformance(mockContextHandle);

        mockFacade.Verify(f => f.ResetContextPerformance(mockContextHandle), Times.Once);
    }

    [Fact]
    public void Facade_GetModelSizeBytes_ReturnsConfiguredValue()
    {
        var mockFacade = new Mock<IBackendFacade>();
        var mockModelHandle = new Mock<IModelHandle>().Object;
        mockFacade
            .Setup(f => f.GetModelSizeBytes(mockModelHandle))
            .Returns(5_000_000_000UL);

        ulong size = mockFacade.Object.GetModelSizeBytes(mockModelHandle);

        Assert.Equal(5_000_000_000UL, size);
        mockFacade.Verify(f => f.GetModelSizeBytes(mockModelHandle), Times.Once);
    }

    [Fact]
    public async Task ModelManager_GetModelsPoolTelemetry_PropagatesModelWeightsBytes()
    {
        var modelProvider = new Mock<IModelProvider>();
        var modelLocator = new Mock<IModelLocator>();
        var queueManager = new Mock<IQueueManager>();

        modelLocator
            .Setup(l => l.ResolvePathsAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedModelPaths("/fake/model.gguf", null, 1024));

        modelProvider
            .Setup(p => p.InitializeAsync(It.IsAny<ModelSettings>(), It.IsAny<ResolvedModelPaths>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        modelProvider
            .Setup(p => p.GetNativeDetails())
            .Returns(new List<NativeModelDetails>
            {
                new()
                {
                    RepoId = "weighted-model",
                    TotalLayers = 32,
                    IdleContextsCount = 1,
                    ModelWeightsBytes = 4_500_000_000
                }
            });

        var manager = new ModelManager(
            modelProvider.Object,
            modelLocator.Object,
            queueManager.Object,
            NullLogger<ModelManager>.Instance);

        try
        {
            await manager.LoadModelAsync(new ModelSettings
            {
                RepoId = "weighted-model",
                MaxContexts = 2,
                Type = ModelType.Llm
            });

            IReadOnlyList<ModelPoolTelemetryDto> telemetry = manager.GetModelsPoolTelemetry();

            Assert.Single(telemetry);
            Assert.Equal(4_500_000_000, telemetry[0].ModelWeightsBytes);
        }
        finally
        {
            manager.Dispose();
        }
    }
}
