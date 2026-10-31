namespace InstantAIGate.Core.Tests.Dashboard;

using InstantAIGate.Core.Dtos.Config;
using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Dtos.Status;
using InstantAIGate.Dashboard.Services;
using System;
using System.Collections.Generic;
using Xunit;

public sealed class MemoryLeakAnalyzerTests
{
    [Fact]
    public void Analyze_WithLoadedModelAndActiveContexts_ComputesWeightsKvAndUnmanagedDiff()
    {
        var snapshot = new DashboardTelemetrySnapshot
        {
            Timestamp = DateTimeOffset.UtcNow,
            GatewayStatus = new GatewayStatusDetails { Status = GatewayOperationalStatus.Ready, ActiveModelId = "qwen3-vl-2b-instruct" },
            InferenceMetrics = new InferenceMetrics(1, 0) { MaxQueueSize = 64 },
            Hardware = new HardwareMetricsDto
            {
                ProcessRamBytes = 3_500_000_000,
                ProcessPrivateBytes = 3_200_000_000,
                ManagedHeapBytes = 200_000_000,
                SystemTotalRamBytes = 32_000_000_000,
                SystemAvailableRamBytes = 20_000_000_000,
                VramTotalBytes = 12_000_000_000,
                VramUsedBytes = 4_000_000_000,
                VramProcessUsedBytes = 3_800_000_000,
                GpuProviderName = "Vulkan"
            },
            Models =
            [
                new ModelPoolTelemetryDto
                {
                    RepoId = "qwen3-vl-2b-instruct",
                    Type = ModelType.Vlm,
                    IsLoaded = true,
                    ActiveLeases = 1,
                    IdlePooledContexts = 1,
                    MaxContexts = 4,
                    GpuLayers = 99,
                    TotalLayers = 28,
                    ContextSize = 4096,
                    KvCacheQuantization = "F16",
                    ModelWeightsBytes = 2_000_000_000
                }
            ],
            Sessions =
            [
                new SessionTelemetryDto
                {
                    SessionId = "s-1",
                    RepoId = "qwen3-vl-2b-instruct",
                    State = SessionStateType.Generating,
                    PastTokens = 256,
                    MaxContextTokens = 4096,
                    HasAllocatedContext = true
                }
            ]
        };

        var report = MemoryLeakAnalyzer.Analyze(snapshot);

        Assert.Equal(2_000_000_000, report.TotalModelWeightsBytes);
        Assert.Equal(2, report.TotalAllocatedContexts);
        Assert.True(report.EstimatedKvCacheBytes > 0);
        Assert.Equal(3_000_000_000, report.PrivateUnmanagedBytes);
        Assert.False(report.IsNativeRetentionSuspected);
        Assert.False(report.IsVramSpilloverRisk);
    }

    [Fact]
    public void Analyze_WhenZeroSessionsAndZeroContextsButProcessVramRetained_FlagsNativeRetention()
    {
        var snapshot = new DashboardTelemetrySnapshot
        {
            Timestamp = DateTimeOffset.UtcNow,
            GatewayStatus = new GatewayStatusDetails { Status = GatewayOperationalStatus.Ready },
            InferenceMetrics = new InferenceMetrics(0, 0),
            Hardware = new HardwareMetricsDto
            {
                ProcessRamBytes = 2_500_000_000,
                ProcessPrivateBytes = 2_400_000_000,
                ManagedHeapBytes = 100_000_000,
                VramTotalBytes = 8_000_000_000,
                VramUsedBytes = 3_200_000_000,
                VramProcessUsedBytes = 2_900_000_000
            },
            Models =
            [
                new ModelPoolTelemetryDto
                {
                    RepoId = "model-2b",
                    IsLoaded = true,
                    ActiveLeases = 0,
                    IdlePooledContexts = 0,
                    MaxContexts = 4,
                    TotalLayers = 24,
                    ContextSize = 4096,
                    ModelWeightsBytes = 2_000_000_000
                }
            ],
            Sessions = []
        };

        var report = MemoryLeakAnalyzer.Analyze(snapshot);

        Assert.True(report.IsNativeRetentionSuspected);
        Assert.NotNull(report.RetentionDiagnosticMessage);
        Assert.Contains("Suspected Native Memory Retention", report.RetentionDiagnosticMessage);
    }

    [Fact]
    public void Analyze_WhenVramExceeds95Percent_FlagsSpilloverRisk()
    {
        var snapshot = new DashboardTelemetrySnapshot
        {
            Timestamp = DateTimeOffset.UtcNow,
            GatewayStatus = new GatewayStatusDetails { Status = GatewayOperationalStatus.Ready },
            InferenceMetrics = new InferenceMetrics(1, 0),
            Hardware = new HardwareMetricsDto
            {
                VramTotalBytes = 10_000_000_000,
                VramUsedBytes = 9_600_000_000
            },
            Models = [],
            Sessions = []
        };

        var report = MemoryLeakAnalyzer.Analyze(snapshot);

        Assert.True(report.IsVramSpilloverRisk);
        Assert.Equal(96.0, report.VramUtilizationPercent, precision: 1);
    }
}
