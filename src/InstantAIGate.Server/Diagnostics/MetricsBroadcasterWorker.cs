namespace InstantAIGate.Server.Diagnostics;

using InstantAIGate.Core.Dtos.Status;
using InstantAIGate.Core.Interfaces.Infrastructure;
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
    private static readonly TimeSpan DashboardTelemetryInterval = TimeSpan.FromSeconds(1);

    private readonly IHubContext<GatewayHub, IGatewayHubClient> _hubContext;
    private readonly IModelManager _modelManager;
    private readonly IMetricsEventSource _eventSource;
    private readonly ISessionInferenceManager _sessionInferenceManager;
    private readonly IGatewayStateManager _stateManager;
    private readonly IHardwareTelemetryProvider _hardwareTelemetryProvider;
    private readonly ILogger<MetricsBroadcasterWorker> _logger;

    public MetricsBroadcasterWorker(
        IHubContext<GatewayHub, IGatewayHubClient> hubContext,
        IModelManager modelManager,
        IMetricsEventSource eventSource,
        ISessionInferenceManager sessionInferenceManager,
        IGatewayStateManager stateManager,
        IHardwareTelemetryProvider hardwareTelemetryProvider,
        ILogger<MetricsBroadcasterWorker> logger)
    {
        _hubContext = hubContext;
        _modelManager = modelManager;
        _eventSource = eventSource;
        _sessionInferenceManager = sessionInferenceManager;
        _stateManager = stateManager;
        _hardwareTelemetryProvider = hardwareTelemetryProvider;
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
            using var dashboardTimer = new PeriodicTimer(DashboardTelemetryInterval);

            Task reactiveMetricsLoop = StreamReactiveMetricsAsync(stoppingToken);
            Task dashboardTelemetryLoop = StreamDashboardTelemetryAsync(dashboardTimer, stoppingToken);

            try
            {
                await Task.WhenAll(reactiveMetricsLoop, dashboardTelemetryLoop);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Graceful shutdown: both loops observe the stopping token.
            }
        }
        finally
        {
            _stateManager.StatusChanged -= OnStatusChanged;
        }
    }

    private async Task StreamReactiveMetricsAsync(CancellationToken stoppingToken)
    {
        await foreach (bool _ in _eventSource.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            var metrics = _modelManager.GetMetrics();
            var nativeDetails = _modelManager.GetNativeDetails();
            await _hubContext.Clients.Group(GatewayHub.AdminGroupName)
                .ReceiveMetrics(metrics, nativeDetails)
                .ConfigureAwait(false);
        }
    }

    private async Task StreamDashboardTelemetryAsync(PeriodicTimer timer, CancellationToken stoppingToken)
    {
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {
                    var snapshot = BuildSnapshot();
                    await _hubContext.Clients.Group(GatewayHub.TelemetryGroupName)
                        .ReceiveDashboardTelemetry(snapshot)
                        .ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Failed to broadcast a dashboard telemetry tick.");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Graceful shutdown.
        }
    }

    private DashboardTelemetrySnapshot BuildSnapshot() => new()
    {
        Timestamp = DateTimeOffset.UtcNow,
        GatewayStatus = _stateManager.GetSnapshot(),
        InferenceMetrics = _modelManager.GetMetrics(),
        Hardware = _hardwareTelemetryProvider.CaptureMetrics(),
        Models = _modelManager.GetModelsPoolTelemetry(),
        Sessions = _sessionInferenceManager.GetSessionsTelemetry()
    };
}