using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Dtos.Session;
using InstantAIGate.Core.Dtos.Status;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace InstantAIGate.Core.Interfaces.Hub;

/// <summary>
/// Defines the server-side hub contract exposed to remote clients.
/// </summary>
public interface IGatewayHub
{
    Task JoinSession(string sessionId, string repoId);
    Task LeaveSession(string sessionId);
    Task DestroySession(string sessionId);
    Task SendPromptDelta(string sessionId, ChatMessage deltaMessage);
    Task RollbackSession(string sessionId, int targetPosition);
    Task ShiftSessionCache(string sessionId, int startPos, int count);
    Task<int> GetSessionTokenCount(string sessionId);
    Task<GatewayStatusDetails> GetGatewayStatus();
}