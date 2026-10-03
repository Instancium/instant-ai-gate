namespace InstantAIGate.Server.Hubs;

using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Dtos.Session;
using InstantAIGate.Core.Exceptions;
using InstantAIGate.SSR.Dtos;
using System.Threading.Tasks;

public interface IGatewayHubClient
{
    // Data Plane
    Task ReceiveTokenDelta(SessionTokenDelta delta);
    Task ReceiveError(string message);
    Task ReceiveContextOverflow(ContextOverflowException payload);
    Task ReceiveSessionClosed(string sessionId);

    // User Observability
    Task ReceiveDownloadProgress(DownloadProgress progress);
    Task ReceiveQueuePosition(int position);

    // System Observability
    Task ReceiveLog(string level, string category, string message);
    Task ReceiveMetrics(InferenceMetrics metrics, object nativeDetails);
}