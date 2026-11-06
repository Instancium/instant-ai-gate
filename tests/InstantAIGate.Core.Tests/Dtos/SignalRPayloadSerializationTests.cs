namespace InstantAIGate.Core.Tests.Dtos;

using InstantAIGate.Core.Dtos.Config;
using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Dtos.Session;
using InstantAIGate.Core.Dtos.Status;
using InstantAIGate.Core.Exceptions;
using InstantAIGate.SSR.Dtos;
using System;
using System.Collections.Generic;
using System.Text.Json;
using Xunit;

public sealed class SignalRPayloadSerializationTests
{
    private static string SerializeAsWireArgument(object payload) =>
        JsonSerializer.Serialize<object>(payload);

    public static TheoryData<object> HubPayloads() => new()
    {
        new SessionTokenDelta("sess", "chunk", false, null),
        new SessionTokenDelta("sess", string.Empty, true, "stop"),
        new ContextOverflowReport("sess", 3870, 200, 256, 4096),
        new GatewayStatusDetails { Status = GatewayOperationalStatus.Ready, ActiveModelId = "model" },
        new DownloadProgress("model", 1024, 2048, 1.0, 50.0f),
        new InferenceMetrics(1, 0),
        new NativeModelDetails { RepoId = "model", ContextSize = 4096 },
        new SessionTelemetryDto
        {
            SessionId = "sess-1",
            RepoId = "model-8b",
            State = SessionStateType.Generating,
            PastTokens = 512,
            MaxContextTokens = 4096,
            PromptTps = 120.5,
            EvalTps = 42.3,
            CreatedAt = DateTimeOffset.UtcNow,
            LastAccessedAt = DateTimeOffset.UtcNow,
            IdleSeconds = 0.0,
            HasAllocatedContext = true,
            BoundConnectionId = "conn-abc"
        },
        new ModelPoolTelemetryDto
        {
            RepoId = "model-8b",
            Type = ModelType.Llm,
            IsLoaded = true,
            IsDraining = false,
            ActiveLeases = 2,
            IdlePooledContexts = 1,
            MaxContexts = 4,
            GpuLayers = 99,
            TotalLayers = 36,
            ContextSize = 4096,
            KvCacheQuantization = "Q8_0",
            ModelWeightsBytes = 5_000_000_000
        },
        new HardwareMetricsDto
        {
            ProcessRamBytes = 2_000_000_000,
            ManagedHeapBytes = 150_000_000,
            SystemTotalRamBytes = 32_000_000_000,
            SystemAvailableRamBytes = 16_000_000_000,
            CpuUsagePercent = 45.2,
            VramTotalBytes = 12_000_000_000,
            VramUsedBytes = 8_500_000_000,
            VramProcessUsedBytes = 6_200_000_000,
            GpuProviderName = "Vulkan (AMD Radeon RX 7900)"
        },
        new DashboardTelemetrySnapshot
        {
            Timestamp = DateTimeOffset.UtcNow,
            GatewayStatus = new GatewayStatusDetails { Status = GatewayOperationalStatus.Ready, ActiveModelId = "model" },
            InferenceMetrics = new InferenceMetrics(2, 0),
            Hardware = new HardwareMetricsDto
            {
                ProcessRamBytes = 1_000_000_000,
                ManagedHeapBytes = 100_000_000,
                SystemTotalRamBytes = 16_000_000_000,
                SystemAvailableRamBytes = 8_000_000_000,
                CpuUsagePercent = 30.0,
                VramTotalBytes = 8_000_000_000,
                VramUsedBytes = 4_000_000_000,
                VramProcessUsedBytes = 3_500_000_000,
                GpuProviderName = "CPU / Unified Memory"
            },
            Models = new List<ModelPoolTelemetryDto>
            {
                new()
                {
                    RepoId = "model-8b",
                    Type = ModelType.Llm,
                    IsLoaded = true,
                    IsDraining = false,
                    ActiveLeases = 1,
                    IdlePooledContexts = 2,
                    MaxContexts = 4,
                    GpuLayers = 99,
                    TotalLayers = 36,
                    ContextSize = 4096,
                    KvCacheQuantization = "F16",
                    ModelWeightsBytes = 0
                }
            },
            Sessions = new List<SessionTelemetryDto>
            {
                new()
                {
                    SessionId = "sess-1",
                    RepoId = "model-8b",
                    State = SessionStateType.Idle,
                    PastTokens = 100,
                    MaxContextTokens = 4096,
                    PromptTps = 0,
                    EvalTps = 0,
                    CreatedAt = DateTimeOffset.UtcNow,
                    LastAccessedAt = DateTimeOffset.UtcNow,
                    IdleSeconds = 5.0,
                    HasAllocatedContext = true,
                    BoundConnectionId = null
                }
            }
        }
    };

    [Theory]
    [MemberData(nameof(HubPayloads))]
    public void AllHubPayloads_AreSystemTextJsonSerializable(object payload)
    {
        var exception = Record.Exception(() => SerializeAsWireArgument(payload));
        Assert.Null(exception);
    }

    [Fact]
    public void ContextOverflowReport_RoundTrips_PreservingAllFields()
    {
        var original = new ContextOverflowReport("sess-1", 3870, 200, 256, 4096);
        var json = SerializeAsWireArgument(original);
        var restored = JsonSerializer.Deserialize<ContextOverflowReport>(json);
        Assert.Equal(original, restored);
    }

    [Fact]
    public void ContextOverflowException_IsNotWireSafe_LocksInReportRationale()
    {
        ContextOverflowException ex;
        try
        {
            throw new ContextOverflowException("sess-1", 3870, 200, 256, 4096);
        }
        catch (ContextOverflowException caught)
        {
            ex = caught;
        }

        Assert.ThrowsAny<NotSupportedException>(() => SerializeAsWireArgument(ex));
    }

    [Fact]
    public void Report_Exception_RoundTrip_PreservesAllFields()
    {
        var ex = new ContextOverflowException("sess-9", 100, 20, 30, 4096);
        var restored = ContextOverflowReport.FromException(ex).ToException();

        Assert.Equal(ex.SessionId, restored.SessionId);
        Assert.Equal(ex.PastTokens, restored.PastTokens);
        Assert.Equal(ex.IncomingTokens, restored.IncomingTokens);
        Assert.Equal(ex.ReservedTokens, restored.ReservedTokens);
        Assert.Equal(ex.ContextSize, restored.ContextSize);
    }

    [Fact]
    public void DashboardTelemetrySnapshot_RoundTrips_WithNestedCollections()
    {
        var snapshot = new DashboardTelemetrySnapshot
        {
            Timestamp = DateTimeOffset.UtcNow,
            GatewayStatus = new GatewayStatusDetails { Status = GatewayOperationalStatus.Ready },
            InferenceMetrics = new InferenceMetrics(0, 0),
            Hardware = new HardwareMetricsDto { GpuProviderName = "Test" },
            Models = [],
            Sessions = []
        };

        var json = SerializeAsWireArgument(snapshot);
        var restored = JsonSerializer.Deserialize<DashboardTelemetrySnapshot>(json);

        Assert.NotNull(restored);
        Assert.Equal(snapshot.GatewayStatus.Status, restored.GatewayStatus.Status);
        Assert.Empty(restored.Models);
        Assert.Empty(restored.Sessions);
    }
}
