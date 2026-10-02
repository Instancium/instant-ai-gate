namespace InstantAIGate.Server.Hubs;

using InstantAIGate.Core.Dtos.Session;
using InstantAIGate.Core.Exceptions;
using System.Threading.Tasks;

public interface ISessionChatClient
{
    Task ReceiveTokenDelta(SessionTokenDelta delta);
    Task ReceiveError(string message);
    Task ReceiveContextOverflow(ContextOverflowException payload);
    Task ReceiveSessionClosed(string sessionId);
}