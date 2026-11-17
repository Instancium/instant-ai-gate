namespace InstantAIGate.Dashboard.Services;

using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Single execution point for every user-initiated dashboard command.
/// Why: before this service existed, each button handler had its own try/catch and a page-top
/// error banner that was invisible at the click site; an uncaught exception anywhere faulted
/// the whole Blazor circuit and silently killed all controls.
/// Guarantees: (1) no exception ever escapes <see cref="RunAsync"/> — failures become
/// <see cref="UiCommandStatus.Failed"/> state + journal record + toast event;
/// (2) the same command id cannot execute concurrently (duplicate starts are rejected);
/// (3) terminal transitions are published via <see cref="CommandCompleted"/> for global feedback.
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

    /// <summary>Raised on every terminal command transition (succeeded or failed).</summary>
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
            return false;
        }

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