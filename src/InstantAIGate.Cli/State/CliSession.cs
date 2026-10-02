namespace InstantAIGate.Cli.State;

using InstantAIGate.Core.Dtos.Config;
using InstantAIGate.Core.Dtos.Inference;
using System;
using System.Collections.Generic;

public class CliSession
{
    public string SessionId { get; set; } = $"cli-{Guid.NewGuid():N}";

    public bool IsRemoteMode { get; set; }
    public string? ActiveModelId { get; set; }
    public ModelSettings? ActiveModelConfig { get; set; }

    public List<ChatMessage> ChatHistory { get; } = new();
    public List<MessageContent> PendingMedia { get; } = new();
    public bool IsExitRequested { get; set; }

    public void ClearHistory()
    {
        ChatHistory.Clear();
        PendingMedia.Clear();
        SessionId = $"cli-{Guid.NewGuid():N}";
    }
}