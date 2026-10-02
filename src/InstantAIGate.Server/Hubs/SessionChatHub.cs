namespace InstantAIGate.Server.Hubs;

using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Dtos.Session;
using InstantAIGate.Core.Exceptions;
using InstantAIGate.Core.Interfaces.Inference;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

[Authorize]
public class SessionChatHub : Hub<ISessionChatClient>
{
    private readonly ISessionInferenceManager _sessionManager;
    private readonly IInferenceEngine _inferenceEngine;
    private readonly ILogger<SessionChatHub> _logger;

    public SessionChatHub(
        ISessionInferenceManager sessionManager,
        IInferenceEngine inferenceEngine,
        ILogger<SessionChatHub> logger)
    {
        _sessionManager = sessionManager;
        _inferenceEngine = inferenceEngine;
        _logger = logger;
    }

    public async Task JoinSession(string sessionId, string repoId)
    {
        try
        {
            var request = new SessionStartRequest(sessionId, repoId);
            await _sessionManager.CreateSessionAsync(request, Context.ConnectionAborted);
            Context.Items["SessionId"] = sessionId;
            _logger.LogInformation("Client {ConnectionId} joined stateful session {SessionId}", Context.ConnectionId, sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize session {SessionId}", sessionId);
            await Clients.Caller.ReceiveError(ex.Message);
        }
    }

    public async Task LeaveSession(string sessionId)
    {
        _logger.LogInformation("Client explicitly requested release of session {SessionId}", sessionId);
        await _sessionManager.ReleaseSessionAsync(sessionId, Context.ConnectionAborted);
        await Clients.Caller.ReceiveSessionClosed(sessionId);
    }

    public async Task SendPromptDelta(string sessionId, ChatMessage deltaMessage)
    {
        try
        {
            await foreach (var token in _inferenceEngine.StreamDeltaGenerationAsync(
                sessionId,
                deltaMessage,
                overrideSettings: null,
                Context.ConnectionAborted))
            {
                await Clients.Caller.ReceiveTokenDelta(new SessionTokenDelta(sessionId, token));
            }

            await Clients.Caller.ReceiveTokenDelta(new SessionTokenDelta(sessionId, string.Empty, IsDone: true, FinishReason: "stop"));
        }
        catch (ContextOverflowException ex)
        {
            _logger.LogWarning(ex, "Context overflow detected for session {SessionId}: Past={Past}, Incoming={Incoming}, Reserve={Reserve}, Limit={Limit}",
                ex.SessionId, ex.PastTokens, ex.IncomingTokens, ex.ReservedTokens, ex.ContextSize);

            await Clients.Caller.ReceiveContextOverflow(ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating delta for session {SessionId}", sessionId);
            await Clients.Caller.ReceiveError($"Inference error: {ex.Message}");
        }
    }

    public async Task RollbackSession(string sessionId, int targetPosition)
    {
        try
        {
            await _sessionManager.RollbackToPositionAsync(sessionId, targetPosition, Context.ConnectionAborted);
            _logger.LogDebug("Session {SessionId} rolled back to position {TargetPosition}", sessionId, targetPosition);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rollback session {SessionId} to position {TargetPosition}", sessionId, targetPosition);
            await Clients.Caller.ReceiveError($"Rollback error: {ex.Message}");
        }
    }

    public async Task ShiftSessionCache(string sessionId, int startPos, int count)
    {
        try
        {
            await _sessionManager.ShiftMemoryRangeAsync(sessionId, startPos, count, Context.ConnectionAborted);
            _logger.LogDebug("Session {SessionId} shifted KV cache: start={StartPos}, count={Count}", sessionId, startPos, count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to shift cache for session {SessionId}: start={StartPos}, count={Count}", sessionId, startPos, count);
            await Clients.Caller.ReceiveError($"Shift error: {ex.Message}");
        }
    }

    public Task<int> GetSessionTokenCount(string sessionId)
    {
        return Task.FromResult(_sessionManager.GetPastTokensCount(sessionId));
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (Context.Items.TryGetValue("SessionId", out var sessionIdObj) && sessionIdObj is string sessionId)
        {
            _logger.LogInformation("Connection {ConnectionId} lost. Releasing Native State session {SessionId}", Context.ConnectionId, sessionId);
            await _sessionManager.ReleaseSessionAsync(sessionId, CancellationToken.None);
            await Clients.Caller.ReceiveSessionClosed(sessionId);
        }

        await base.OnDisconnectedAsync(exception);
    }
}