namespace InstantAIGate.Core.Tests.Server;

using System.Collections.Generic;
using InstantAIGate.Server.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

public sealed class HardwareProfilePolicyTests
{
    private static HardwareProfilePolicy CreatePolicy(Dictionary<string, string?> overrides)
    {
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(overrides)
            .Build();
        return new HardwareProfilePolicy(config, NullLogger<HardwareProfilePolicy>.Instance);
    }

    [Fact]
    public void ResolveProfileName_EmbeddingModelWithChatProfile_AutoRoutesToEmbedding()
    {
        var policy = CreatePolicy(new Dictionary<string, string?>
        {
            ["InstantAIGate:HardwareProfiles:Default:ContextSize"] = "4096",
            ["InstantAIGate:HardwareProfiles:Embedding:ContextSize"] = "512",
            ["InstantAIGate:HardwareProfiles:Embedding:Embeddings"] = "true"
        });

        string resolved = policy.ResolveProfileName("Default", isEmbeddingModel: true);

        Assert.Equal(HardwareProfilePolicy.EmbeddingProfileName, resolved);
    }

    [Fact]
    public void ResolveProfileName_EmbeddingModelWithoutEmbeddingProfile_FallsBackToRequested()
    {
        var policy = CreatePolicy(new Dictionary<string, string?>
        {
            ["InstantAIGate:HardwareProfiles:Default:ContextSize"] = "4096"
        });

        string resolved = policy.ResolveProfileName("Default", isEmbeddingModel: true);

        Assert.Equal("Default", resolved);
    }

    [Fact]
    public void ResolveProfileName_ChatModel_KeepsRequestedProfile()
    {
        var policy = CreatePolicy(new Dictionary<string, string?>
        {
            ["InstantAIGate:HardwareProfiles:Default:ContextSize"] = "4096",
            ["InstantAIGate:HardwareProfiles:Embedding:Embeddings"] = "true"
        });

        Assert.Equal("Default", policy.ResolveProfileName("Default", isEmbeddingModel: false));
    }

    [Fact]
    public void ResolveProfileName_EmbeddingCapableRequestedProfile_IsNotOverridden()
    {
        var policy = CreatePolicy(new Dictionary<string, string?>
        {
            ["InstantAIGate:HardwareProfiles:CustomEmb:Embeddings"] = "true",
            ["InstantAIGate:HardwareProfiles:Embedding:Embeddings"] = "true"
        });

        Assert.Equal("CustomEmb", policy.ResolveProfileName("CustomEmb", isEmbeddingModel: true));
    }

    [Fact]
    public void IsEmbeddingModel_DetectsByRepoIdAndArchitecture()
    {
        Assert.True(HardwareProfilePolicy.IsEmbeddingModel("qwen3-embedding-0.6b"));
        Assert.True(HardwareProfilePolicy.IsEmbeddingModel("some-model", "bert-embedding"));
        Assert.False(HardwareProfilePolicy.IsEmbeddingModel("qwen3-vl-8b-instruct"));
        Assert.False(HardwareProfilePolicy.IsEmbeddingModel(string.Empty));
    }
}
