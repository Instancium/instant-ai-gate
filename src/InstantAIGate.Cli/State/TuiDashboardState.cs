using InstantAIGate.Core.Dtos.Inference;
using System;
using System.Collections.Concurrent;
using System.Text;

namespace InstantAIGate.Cli.State;

public class TuiDashboardState
{
    // Zone A: Header & Context
    public string ActiveModelId { get; set; } = "None";
    public string ConnectionMode { get; set; } = "Local";
    public int ContextSize { get; set; } = 4096;

    // Zone B: Telemetry (Engine Status)
    public InferenceMetrics LastMetrics { get; set; } = new(0, 0);
    public double TokensPerSecond { get; set; }
    public string EngineStatus { get; set; } = "IDLE";

    // Zone C: Chat History
    public ConcurrentQueue<ChatMessage> ChatHistory { get; } = new();

    // Zone D: Input & Media
    public StringBuilder InputBuffer { get; } = new();
    public ConcurrentBag<MessageContent> PendingMedia { get; } = new();

    // Thread-safe event for UI refresh triggers
    public event Action? OnStateChanged;

    public void NotifyUpdate() => OnStateChanged?.Invoke();

    public void ClearInput()
    {
        InputBuffer.Clear();
        PendingMedia.Clear();
        NotifyUpdate();
    }
}