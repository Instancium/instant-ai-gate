namespace InstantAIGate.Server.Diagnostics;

using InstantAIGate.Core.Interfaces.Inference;
using InstantAIGate.Server.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Background service that reactively broadcasts inference metrics and native hardware details
/// to connected administrative clients via SignalR when state mutations occur.
/// </summary>
public sealed class MetricsBroadcasterWorker : BackgroundService
{
    private readonly IHubContext<GatewayHub, IGatewayHubClient> _hubContext;
    private readonly IModelManager _modelManager;
    private readonly IMetricsEventSource _eventSource;
    private readonly ILogger<MetricsBroadcasterWorker> _logger;

    public MetricsBroadcasterWorker(
        IHubContext<GatewayHub, IGatewayHubClient> hubContext,
        IModelManager modelManager,
        IMetricsEventSource eventSource,
        ILogger<MetricsBroadcasterWorker> logger)
    {
        _hubContext = hubContext;
        _modelManager = modelManager;
        _eventSource = eventSource;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Reactive Metrics Broadcaster Worker started.");

        var reader = _eventSource.Reader;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await reader.WaitToReadAsync(stoppingToken))
                {
                    break;
                }

                // Coalesce all buffered triggers into a single latest snapshot broadcast
                while (reader.TryRead(out _))
                {
                }

                var metrics = _modelManager.GetMetrics();
                var nativeDetails = _modelManager.GetNativeDetails();

                await _hubContext.Clients.Group(GatewayHub.AdminGroupName)
                    .ReceiveMetrics(metrics, nativeDetails);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogTrace(ex, "Failed to broadcast reactive metrics to admin group.");
            }
        }
    }
}