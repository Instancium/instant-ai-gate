using InstantAIGate.Core.Dtos.Config;
using InstantAIGate.Core.Dtos.Inference;
using System.Collections.Generic;

namespace InstantAIGate.Cli.State;

public class CliSession
{
    public string? ActiveModelId { get; set; }
    public ModelSettings? ActiveModelConfig { get; set; }

    public List<ChatMessage> ChatHistory { get; } = new();
    public List<string> PendingImagePaths { get; } = new();
    public bool IsExitRequested { get; set; }

    public void ClearHistory()
    {
        ChatHistory.Clear();
        PendingImagePaths.Clear();
    }
}

public class ModelConfigurationWrapper
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public ModelSettings Config { get; set; } = new();
}