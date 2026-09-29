namespace InstantAIGate.Server.Services.Workers;

using InstantAIGate.Core.Dtos.Config;
using InstantAIGate.Core.Interfaces.Inference;
using InstantAIGate.Server.Configuration;
using InstantAIGate.SSR.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Threading;
using System.Threading.Tasks;

public sealed class ModelStartupWorker : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly StartupModelSettings _startupSettings;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ModelStartupWorker> _logger;

    public ModelStartupWorker(
        IServiceProvider serviceProvider,
        IOptions<StartupModelSettings> startupOptions,
        IConfiguration configuration,
        ILogger<ModelStartupWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _startupSettings = startupOptions.Value;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_startupSettings.AutoLoad)
        {
            _logger.LogInformation("Startup model autoload is disabled.");
            return;
        }

        if (string.IsNullOrWhiteSpace(_startupSettings.RepoId))
        {
            _logger.LogWarning("Autoload is enabled, but no RepoId was specified in configuration.");
            return;
        }

        _logger.LogInformation("Autoloading startup model '{RepoId}' with profile '{Profile}'...",
            _startupSettings.RepoId, _startupSettings.Profile);

        using var scope = _serviceProvider.CreateScope();
        var catalogService = scope.ServiceProvider.GetRequiredService<IModelCatalogService>();
        var modelManager = scope.ServiceProvider.GetRequiredService<IModelManager>();

        var targetModel = await catalogService.FindModelByIdAsync(_startupSettings.RepoId, cancellationToken);
        if (targetModel == null)
        {
            _logger.LogError("Startup model '{RepoId}' was not found in catalog.", _startupSettings.RepoId);
            return;
        }

        var profileName = string.IsNullOrWhiteSpace(_startupSettings.Profile) ? "Default" : _startupSettings.Profile;
        var hwProfile = _configuration.GetSection($"InstantAIGate:HardwareProfiles:{profileName}")
            .Get<HardwareProfileSettings>() ?? new HardwareProfileSettings();

        var config = new ModelSettings
        {
            RepoId = targetModel.Id,
            VisionSupport = targetModel.RequiresVisionProjector,
            Type = targetModel.RequiresVisionProjector ? ModelType.Vlm : ModelType.Llm,
            GpuLayerCount = hwProfile.GpuLayerCount,
            MainGPU = hwProfile.MainGPU,
            ContextSize = hwProfile.ContextSize,
            BatchSize = hwProfile.BatchSize,
            Threads = hwProfile.Threads,
            FlashAttention = hwProfile.FlashAttention,
            Embeddings = hwProfile.Embeddings,
            KvCacheQuantization = hwProfile.KvCacheQuantization,
            UseMemoryLock = hwProfile.UseMemoryLock,
            MaxContexts = hwProfile.MaxContexts
        };

        try
        {
            await modelManager.LoadModelAsync(config, cancellationToken);
            _logger.LogInformation("Startup model '{RepoId}' successfully loaded into memory.", _startupSettings.RepoId);
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Failed to autoload startup model '{RepoId}'.", _startupSettings.RepoId);
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}