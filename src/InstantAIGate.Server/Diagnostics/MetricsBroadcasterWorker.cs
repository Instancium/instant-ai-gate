namespace InstantAIGate.Server.Diagnostics;

using InstantAIGate.Core.Dtos.Status;
using InstantAIGate.Core.Interfaces.Inference;
using InstantAIGate.Server.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

public sealed class MetricsBroadcasterWorker : BackgroundService
{
    private readonly IHubContext<GatewayHub, IGatewayHubClient> _hubContext;
    private readonly IModelManager _modelManager;
    private readonly IMetricsEventSource _eventSource;
    private readonly IGatewayStateManager _stateManager;
    private readonly ILogger<MetricsBroadcasterWorker> _logger;

    public MetricsBroadcasterWorker(
        IHubContext<GatewayHub, IGatewayHubClient> hubContext,
        IModelManager modelManager,
        IMetricsEventSource eventSource,
        IGatewayStateManager stateManager,
        ILogger<MetricsBroadcasterWorker> logger)
    {
        _hubContext = hubContext;
        _modelManager = modelManager;
        _eventSource = eventSource;
        _stateManager = stateManager;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Reactive Metrics Broadcaster Worker started.");

        DateTimeOffset lastStatusBroadcast = DateTimeOffset.MinValue;
        GatewayOperationalStatus lastBroadcastStatus = GatewayOperationalStatus.Uninitialized;

        void OnStatusChanged(GatewayStatusDetails details)
        {
            var now = DateTimeOffset.UtcNow;
            if (details.Status != lastBroadcastStatus || (now - lastStatusBroadcast).TotalMilliseconds >= 1000)
            {
                lastBroadcastStatus = details.Status;
                lastStatusBroadcast = now;
                _ = _hubContext.Clients.All.ReceiveGatewayStatus(details);
            }
        }

        _stateManager.StatusChanged += OnStatusChanged;

        try
        {
            var reader = _eventSource.Reader;
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (!await reader.WaitToReadAsync(stoppingToken))
                    {
                        break;
                    }

                    while (reader.TryRead(out _)) { }

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
        finally
        {
            _stateManager.StatusChanged -= OnStatusChanged;
        }
    }
}