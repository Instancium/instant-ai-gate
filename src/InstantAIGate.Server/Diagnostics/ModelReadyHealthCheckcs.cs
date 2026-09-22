using InstantAIGate.Core.Interfaces.Inference;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Threading;
using System.Threading.Tasks;

namespace InstantAIGate.Server.Diagnostics;

public class ModelReadyHealthCheck : IHealthCheck
{
    private readonly IModelManager _modelManager;

    public ModelReadyHealthCheck(IModelManager modelManager)
    {
        _modelManager = modelManager;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var activeConfig = _modelManager.GetActiveSettings();

        if (activeConfig != null)
        {
            return Task.FromResult(HealthCheckResult.Healthy($"Model '{activeConfig.RepoId}' is loaded and ready."));
        }

        return Task.FromResult(HealthCheckResult.Unhealthy("No active model is loaded in memory."));
    }
}