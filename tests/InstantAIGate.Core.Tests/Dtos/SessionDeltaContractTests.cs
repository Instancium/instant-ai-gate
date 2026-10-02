namespace InstantAIGate.Core.Tests.Dtos;

using InstantAIGate.Core.Dtos.Config;
using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Dtos.Session;
using System.Text.Json;
using Xunit;

public class SessionDeltaContractTests
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public void SessionStartRequest_ShouldSerializeAndDeserialize()
    {
        var original = new SessionStartRequest("sess-001", "qwen2.5-7b", new InferenceSettings { MaxTokens = 1024 });

        var json = JsonSerializer.Serialize(original, _jsonOptions);
        var deserialized = JsonSerializer.Deserialize<SessionStartRequest>(json, _jsonOptions);

        Assert.NotNull(deserialized);
        Assert.Equal("sess-001", deserialized.SessionId);
        Assert.Equal("qwen2.5-7b", deserialized.RepoId);
        Assert.NotNull(deserialized.Settings);
        Assert.Equal(1024, deserialized.Settings.MaxTokens);
    }

    [Fact]
    public void SessionPromptDelta_ShouldSerializeAndDeserialize()
    {
        var message = new ChatMessage("user", "Hello with delta!");
        var original = new SessionPromptDelta("sess-002", message);

        var json = JsonSerializer.Serialize(original, _jsonOptions);
        var deserialized = JsonSerializer.Deserialize<SessionPromptDelta>(json, _jsonOptions);

        Assert.NotNull(deserialized);
        Assert.Equal("sess-002", deserialized.SessionId);
        Assert.Equal("user", deserialized.Message.Role);
        Assert.Equal("Hello with delta!", deserialized.Message.Content);
    }

    [Fact]
    public void SessionTokenDelta_ShouldSerializeAndDeserialize()
    {
        var original = new SessionTokenDelta("sess-003", " world", IsDone: true, FinishReason: "stop");

        var json = JsonSerializer.Serialize(original, _jsonOptions);
        var deserialized = JsonSerializer.Deserialize<SessionTokenDelta>(json, _jsonOptions);

        Assert.NotNull(deserialized);
        Assert.Equal("sess-003", deserialized.SessionId);
        Assert.Equal(" world", deserialized.Content);
        Assert.True(deserialized.IsDone);
        Assert.Equal("stop", deserialized.FinishReason);
    }
}