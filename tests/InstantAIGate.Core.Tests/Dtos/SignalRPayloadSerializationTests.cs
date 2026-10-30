namespace InstantAIGate.Core.Tests.Dtos;

using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Dtos.Session;
using InstantAIGate.Core.Dtos.Status;
using InstantAIGate.Core.Exceptions;
using InstantAIGate.SSR.Dtos;
using System;
using System.Text.Json;
using Xunit;

public sealed class SignalRPayloadSerializationTests
{
    // Mirrors JsonHubProtocol behavior: arguments are serialized as object,
    // so System.Text.Json resolves the RUNTIME type — exactly like the wire.
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
        new NativeModelDetails { RepoId = "model", ContextSize = 4096 }
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
        // Locks the rationale: once an exception is thrown, TargetSite is populated
        // with MethodBase, which System.Text.Json rejects with NotSupportedException.
        // If this ever stops throwing, ContextOverflowReport can be retired in favor of the exception.
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
}
