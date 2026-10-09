using InstantAIGate.Core.Interfaces.Inference;
using InstantAIGate.Core.Interfaces.Session;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace InstantAIGate.Core.Services.Session;

/// <summary>
/// Background service that periodically reaps orphaned ephemeral sessions.
/// </summary>
public sealed class SessionReaperService : BackgroundService, ISessionReaperService
{
    private readonly IEphemeralSessionRegistry _ephemeralSessionRegistry;
    private readonly ISessionInferenceManager _sessionInferenceManager;
    private readonly ILogger<SessionReaperService> _logger;
    private readonly TimeProvider _timeProvider;
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan OrphanThreshold = TimeSpan.FromMinutes(2);

    public SessionReaperService(
        IEphemeralSessionRegistry ephemeralSessionRegistry,
        ISessionInferenceManager sessionInferenceManager,
        ILogger<SessionReaperService> logger,
        TimeProvider? timeProvider = null)
    {
        _ephemeralSessionRegistry = ephemeralSessionRegistry;
        _sessionInferenceManager = sessionInferenceManager;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SessionReaperService started with cleanup interval {Interval}.", CleanupInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(CleanupInterval, _timeProvider, stoppingToken);
                await CleanupOrphanedSessionsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception during ephemeral session cleanup.");
            }
        }

        _logger.LogInformation("SessionReaperService stopped.");
    }

    public async Task CleanupOrphanedSessionsAsync(CancellationToken cancellationToken)
    {
        var orphanedSessions = _ephemeralSessionRegistry.DrainOrphanedSessions(OrphanThreshold);
        if (orphanedSessions.Count == 0)
        {
            return;
        }

        _logger.LogWarning("Reaper detected {Count} orphaned ephemeral sessions. Forcing VRAM release.", orphanedSessions.Count);

        foreach (var sessionId in orphanedSessions)
        {
            try
            {
                await _sessionInferenceManager.ReleaseSessionAsync(sessionId, destroySlot: true, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to force-release orphaned session {SessionId}.", sessionId);
            }
        }
    }
}