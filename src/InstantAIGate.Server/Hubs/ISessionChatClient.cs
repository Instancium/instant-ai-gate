namespace InstantAIGate.Server.Hubs;

using InstantAIGate.Core.Dtos.Session;
using System.Threading.Tasks;

public interface ISessionChatClient
{
    Task ReceiveTokenDelta(SessionTokenDelta delta);

    Task ReceiveError(string message);

    Task ReceiveSessionClosed(string sessionId);
}