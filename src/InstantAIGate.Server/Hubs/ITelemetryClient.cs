using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.SSR.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;

namespace InstantAIGate.Server.Hubs;

public interface ITelemetryClient
{
    Task ReceiveLog(string level, string category, string message);
    Task ReceiveMetrics(InferenceMetrics metrics, object nativeDetails);
    Task ReceiveSsrProgress(DownloadProgress progress);
}

[Authorize(Roles = "Admin")]
public class TelemetryHub : Hub<ITelemetryClient>
{
    // Clients connect and receive streams automatically.
    // Explicit client-to-server methods can be added here if needed.
}