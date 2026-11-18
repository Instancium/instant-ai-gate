namespace InstantAIGate.Dashboard.Services;

using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Single execution point for every user-initiated dashboard command.
/// Guarantees: (1) no exception escapes <see cref="RunAsync"/>; (2) the same command id
/// cannot execute concurrently and a rejected duplicate is reported, never swallowed;
/// (3) both the running transition and the terminal transition are published so buttons
/// can render a spinner while the RPC is in flight.
/// </summary>
public sealed class UiCommandService
{
    public const int JournalCapacity = 50;

    private readonly ConcurrentDictionary<string, bool> _running = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, UiCommandState> _states = new(StringComparer.Ordinal);
    private readonly CircularBuffer<OperationRecord> _journal = new(JournalCapacity);
    private readonly ILogger<UiCommandService> _logger;

    public UiCommandService(ILogger<UiCommandService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Raised on every command transition: Running at start, then Succeeded or Failed.
    /// Toast surfaces must ignore Running; state surfaces (buttons, page) must react to it.
    /// </summary>
    public event Action<UiCommandEvent>? CommandCompleted;

    public UiCommandState GetState(string commandId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        return _states.TryGetValue(commandId, out UiCommandState? state) ? state : UiCommandState.Idle;
    }

    public bool IsRunning(string commandId) => GetState(commandId).IsRunning;

    /// <summary>Chronological snapshot of the last <see cref="JournalCapacity"/> operations.</summary>
    public IReadOnlyList<OperationRecord> GetJournal() => _journal.Snapshot();

    /// <summary>
    /// Executes a command with full feedback bookkeeping.
    /// Returns true on success; false when the action threw, was cancelled, or a duplicate
    /// execution of the same command id was already in flight.
    /// </summary>
    public async Task<bool> RunAsync(
        string commandId,
        string label,
        Func<CancellationToken, Task> action,
        string? successMessage = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(action);

        if (!_running.TryAdd(commandId, true))
        {
            _logger.LogDebug("Command {CommandId} rejected: already running.", commandId);
            NotifyDuplicate(commandId, label);
            return false;
        }

        PublishRunning(commandId, label);

        try
        {
            await action(ct);
            Record(commandId, label, UiCommandStatus.Succeeded, successMessage ?? $"{label} completed.");
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            Record(commandId, label, UiCommandStatus.Failed, $"{label} cancelled.");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Command {CommandId} ({Label}) failed.", commandId, label);
            Record(commandId, label, UiCommandStatus.Failed, ex.Message);
            return false;
        }
        finally
        {
            _running.TryRemove(commandId, out _);
        }
    }

    /// <summary>
    /// Publishes the in-flight state without touching the audit journal.
    /// Why: without this transition IsRunning was permanently false, so no button ever
    /// showed a spinner and a slow admin RPC was indistinguishable from a dead control.
    /// </summary>
    private void PublishRunning(string commandId, string label)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        _states[commandId] = new UiCommandState
        {
            Status = UiCommandStatus.Running,
            LastMessage = null,
            LastCompletedAt = null
        };

        CommandCompleted?.Invoke(new UiCommandEvent(commandId, label, UiCommandStatus.Running, null, now));
    }

    /// <summary>
    /// Reports a rejected duplicate start. Deliberately does not overwrite the state of the
    /// execution that is still in flight, so its spinner survives the rejected click.
    /// </summary>
    private void NotifyDuplicate(string commandId, string label)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string message = $"{label} is already running. Wait for it to finish.";

        _journal.Add(new OperationRecord(now, commandId, label, UiCommandStatus.Failed, message));
        CommandCompleted?.Invoke(new UiCommandEvent(commandId, label, UiCommandStatus.Failed, message, now));
    }

    private void Record(string commandId, string label, UiCommandStatus status, string? message)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        _states[commandId] = new UiCommandState
        {
            Status = status,
            LastMessage = message,
            LastCompletedAt = now
        };

        _journal.Add(new OperationRecord(now, commandId, label, status, message));
        CommandCompleted?.Invoke(new UiCommandEvent(commandId, label, status, message, now));
    }
}