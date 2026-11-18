namespace InstantAIGate.Dashboard.Services;

using System;

/// <summary>Lifecycle of a single user-initiated command.</summary>
public enum UiCommandStatus
{
    Idle = 0,
    Running = 1,
    Succeeded = 2,
    Failed = 3
}

/// <summary>Immutable per-command state exposed to buttons for spinner/disabled rendering.</summary>
public sealed record UiCommandState
{
    public static UiCommandState Idle { get; } = new() { Status = UiCommandStatus.Idle };

    public UiCommandStatus Status { get; init; }

    public string? LastMessage { get; init; }

    public DateTimeOffset? LastCompletedAt { get; init; }

    public bool IsRunning => Status == UiCommandStatus.Running;
}

/// <summary>Terminal notification published once per command execution (feeds toasts).</summary>
public sealed record UiCommandEvent(
    string CommandId,
    string Label,
    UiCommandStatus Status,
    string? Message,
    DateTimeOffset Timestamp);

/// <summary>Audit trail entry retained in the in-memory operation journal.</summary>
public sealed record OperationRecord(
    DateTimeOffset Timestamp,
    string CommandId,
    string Label,
    UiCommandStatus Status,
    string? Message);