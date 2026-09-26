using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.SSR.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

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
    // Hub exposes server-to-client telemetry streams only; clients are passive receivers.
}