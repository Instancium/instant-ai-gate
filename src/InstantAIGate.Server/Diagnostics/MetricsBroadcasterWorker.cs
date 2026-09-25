using InstantAIGate.Core.Interfaces.Inference;
using InstantAIGate.Server.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace InstantAIGate.Server.Diagnostics;

public class MetricsBroadcasterWorker : BackgroundService
{
    private readonly IHubContext<TelemetryHub, ITelemetryClient> _hubContext;
    private readonly IModelManager _modelManager;
    private readonly ILogger<MetricsBroadcasterWorker> _logger;

    public MetricsBroadcasterWorker(
        IHubContext<TelemetryHub, ITelemetryClient> hubContext,
        IModelManager modelManager,
        ILogger<MetricsBroadcasterWorker> logger)
    {
        _hubContext = hubContext;
        _modelManager = modelManager;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Metrics Broadcaster Worker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var metrics = _modelManager.GetMetrics();
                var nativeDetails = _modelManager.GetNativeDetails();

                await _hubContext.Clients.All.ReceiveMetrics(metrics, nativeDetails);
            }
            catch (Exception ex)
            {
                _logger.LogTrace(ex, "Failed to broadcast metrics.");
            }

            await Task.Delay(1000, stoppingToken); // Broadcast 1 Hz
        }
    }
}