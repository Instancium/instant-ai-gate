namespace InstantAIGate.Server.Hubs;

using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Dtos.Session;
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


    public async Task LeaveSession(string sessionId)
    {
        _logger.LogInformation("Client explicitly requested release of session {SessionId}", sessionId);
        await _sessionManager.ReleaseSessionAsync(sessionId);
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

    public async Task SendPromptDelta(string sessionId, ChatMessage deltaMessage)
    {
        try
        {
       
            await foreach (var token in _inferenceEngine.StreamDeltaGenerationAsync(sessionId, deltaMessage, overrideSettings: null, Context.ConnectionAborted))
            {
                await Clients.Caller.ReceiveTokenDelta(new SessionTokenDelta(sessionId, token));
            }

    
            await Clients.Caller.ReceiveTokenDelta(new SessionTokenDelta(sessionId, string.Empty, IsDone: true, FinishReason: "stop"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating delta for session {SessionId}", sessionId);
            await Clients.Caller.ReceiveError($"Inference error: {ex.Message}");
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (Context.Items.TryGetValue("SessionId", out var sessionIdObj) && sessionIdObj is string sessionId)
        {
            _logger.LogInformation("Connection lost. Releasing Native State session {SessionId}", sessionId);
            await _sessionManager.ReleaseSessionAsync(sessionId);
            await Clients.Caller.ReceiveSessionClosed(sessionId);
        }

        await base.OnDisconnectedAsync(exception);
    }
}