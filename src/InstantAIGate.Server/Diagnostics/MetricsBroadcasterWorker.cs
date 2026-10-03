namespace InstantAIGate.Server.Diagnostics;

using InstantAIGate.Core.Interfaces.Inference;
using InstantAIGate.Server.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

public class MetricsBroadcasterWorker : BackgroundService
{
    private readonly IHubContext<GatewayHub, IGatewayHubClient> _hubContext;
    private readonly IModelManager _modelManager;
    private readonly ILogger<MetricsBroadcasterWorker> _logger;

    public MetricsBroadcasterWorker(
        IHubContext<GatewayHub, IGatewayHubClient> hubContext,
        IModelManager modelManager,
        ILogger<MetricsBroadcasterWorker> logger)
    {
        _hubContext = hubContext;
        _modelManager = modelManager;
        _logger = logger;
    }


    /// <summary>
    /// Background worker that periodically broadcasts inference metrics and native hardware details 
    /// to connected clients via SignalR at a fixed 1Hz frequency.
    /// <remarks>
    /// Architectural Note: 
    /// This implementation utilizes a polling-to-push approach via <see cref="Task.Delay(int, CancellationToken)"/> 
    /// as a pragmatic Time-to-Market (TTM) trade-off optimized for single-instance server architectures. 
    /// While it centralizes telemetry broadcasting and simplifies security/authorization, it introduces 
    /// a fixed latency (up to 1 second) and consumes constant background CPU cycles.
    /// 
    /// TODO: Refactor toward a fully Event-Driven / Reactive architecture (e.g., IObservable or internal 
    /// domain event buses) to broadcast state changes instantly upon occurrence and eliminate polling overhead.
    /// </remarks>
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Metrics Broadcaster Worker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var metrics = _modelManager.GetMetrics();
                var nativeDetails = _modelManager.GetNativeDetails();
                await _hubContext.Clients.Group(GatewayHub.AdminGroupName)
                    .ReceiveMetrics(metrics, nativeDetails);
            }
            catch (Exception ex)
            {
                _logger.LogTrace(ex, "Failed to broadcast metrics to admin group.");
            }

            await Task.Delay(1000, stoppingToken);
        }
    }
}