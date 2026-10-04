namespace InstantAIGate.Server.Diagnostics;

using InstantAIGate.Core.Dtos.Status;
using InstantAIGate.Core.Interfaces.Inference;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

public class ModelReadyHealthCheck : IHealthCheck
{
    private readonly IModelManager _modelManager;
    private readonly IGatewayStateManager _stateManager;

    public ModelReadyHealthCheck(IModelManager modelManager, IGatewayStateManager stateManager)
    {
        _modelManager = modelManager ?? throw new ArgumentNullException(nameof(modelManager));
        _stateManager = stateManager ?? throw new ArgumentNullException(nameof(stateManager));
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var activeConfig = _modelManager.GetActiveSettings();
        var snapshot = _stateManager.GetSnapshot();

        if (activeConfig != null || snapshot.Status == GatewayOperationalStatus.Ready)
        {
            var modelId = activeConfig?.RepoId ?? snapshot.ActiveModelId ?? "unknown";
            return Task.FromResult(HealthCheckResult.Healthy($"Model '{modelId}' is loaded and ready."));
        }

        var data = new Dictionary<string, object>
        {
            ["status"] = snapshot.Status.ToString(),
            ["activeModelId"] = snapshot.ActiveModelId ?? string.Empty,
            ["stageDescription"] = snapshot.StageDescription ?? string.Empty,
            ["progressPercentage"] = snapshot.ProgressPercentage,
            ["downloadedBytes"] = snapshot.DownloadedBytes,
            ["totalBytes"] = snapshot.TotalBytes,
            ["bytesPerSecond"] = snapshot.BytesPerSecond
        };

        if (snapshot.Status is GatewayOperationalStatus.ModelDownloading or GatewayOperationalStatus.ModelLoading)
        {
            string description = string.Create(
                CultureInfo.InvariantCulture,
                $"{snapshot.StageDescription} ({snapshot.ProgressPercentage:F1}%)");

            return Task.FromResult(HealthCheckResult.Degraded(
                description: description,
                data: data));
        }

        if (snapshot.Status == GatewayOperationalStatus.Faulted)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                description: snapshot.ErrorMessage ?? "Gateway initialization faulted.",
                data: data));
        }

        return Task.FromResult(HealthCheckResult.Unhealthy("No active model is loaded in memory.", data: data));
    }
}