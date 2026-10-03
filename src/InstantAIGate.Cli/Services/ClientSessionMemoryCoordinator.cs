namespace InstantAIGate.Cli.Services;

using InstantAIGate.Cli.Core;
using InstantAIGate.Cli.State;
using InstantAIGate.Core.Dtos.Status;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Reference coordinator demonstrating client-side KV cache lifecycle, 
/// context auditing, sliding window truncation, and checkpoints.
/// </summary>
public sealed class ClientSessionMemoryCoordinator
{
    private readonly IGatewayClient _gatewayClient;
    private readonly CliSession _cliSession;
    private readonly ILogger<ClientSessionMemoryCoordinator> _logger;

    public ClientSessionMemoryCoordinator(
        IGatewayClient gatewayClient,
        CliSession cliSession,
        ILogger<ClientSessionMemoryCoordinator> logger)
    {
        _gatewayClient = gatewayClient ?? throw new ArgumentNullException(nameof(gatewayClient));
        _cliSession = cliSession ?? throw new ArgumentNullException(nameof(cliSession));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Audits current memory consumption and returns a metrics snapshot for the given session.
    /// </summary>
    public async Task<ContextWindowMetrics> AuditContextAsync(
        string sessionId,
        int systemPrefixTokens = 0,
        CancellationToken ct = default)
    {
        var activeModel = await _gatewayClient.GetActiveModelDetailsAsync(ct);
        int pastTokens = await _gatewayClient.GetSessionTokenCountAsync(sessionId, ct);

        int capacity = activeModel?.ContextSize > 0
            ? activeModel.ContextSize
            : _cliSession.ActiveModelConfig?.ContextSize ?? 4096;

        return new ContextWindowMetrics
        {
            SessionId = sessionId,
            RepoId = activeModel?.RepoId ?? _cliSession.ActiveModelId ?? string.Empty,
            ContextCapacity = capacity,
            PastTokensCount = pastTokens,
            SystemPrefixTokensCount = Math.Max(0, systemPrefixTokens)
        };
    }

    /// <summary>
    /// Executes a safe sliding window shift, removing old dialogue turns while keeping system prefix intact.
    /// </summary>
    public async Task<bool> TruncateSlidingWindowAsync(
        string sessionId,
        int systemPrefixTokens,
        int tokensToEvict,
        CancellationToken ct = default)
    {
        if (tokensToEvict <= 0)
        {
            return false;
        }

        int currentPastTokens = await _gatewayClient.GetSessionTokenCountAsync(sessionId, ct);
        int availableHistory = currentPastTokens - systemPrefixTokens;

        if (availableHistory <= 0)
        {
            _logger.LogWarning("No history tokens available to shift in session '{SessionId}'.", sessionId);
            return false;
        }

        int safeEvictionCount = Math.Min(tokensToEvict, availableHistory);
        int startPos = systemPrefixTokens;

        try
        {
            _logger.LogInformation(
                "Applying sliding window to session '{SessionId}': shifting {Count} tokens starting at {StartPos}.",
                sessionId, safeEvictionCount, startPos);

            await _gatewayClient.ShiftSessionMemoryAsync(sessionId, startPos, safeEvictionCount, ct);
            return true;
        }
        catch (NotSupportedException nse)
        {
            _logger.LogWarning(nse, "Physical shift not supported. Falling back to rollback checkpoint.");
            // Fallback: truncate to system prefix boundary
            await RollbackToCheckpointAsync(sessionId, systemPrefixTokens, ct);
            return false;
        }
    }

    /// <summary>
    /// Rolls back the KV cache state to a specific token boundary.
    /// </summary>
    public async Task RollbackToCheckpointAsync(
        string sessionId,
        int checkpointPosition,
        CancellationToken ct = default)
    {
        if (checkpointPosition < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(checkpointPosition), "Checkpoint cannot be negative.");
        }

        _logger.LogInformation(
            "Rolling back session '{SessionId}' memory to token index {Checkpoint}.",
            sessionId, checkpointPosition);

        await _gatewayClient.RollbackSessionAsync(sessionId, checkpointPosition, ct);
    }

    /// <summary>
    /// Explicitly tears down the remote session context and clears local history.
    /// </summary>
    public async Task EvictSessionAsync(string sessionId, CancellationToken ct = default)
    {
        _logger.LogInformation("Evicting session '{SessionId}' from inference context pool.", sessionId);
        await _gatewayClient.EndSessionAsync(sessionId, ct);

        if (_cliSession.SessionId == sessionId)
        {
            _cliSession.ClearHistory();
        }
    }
}