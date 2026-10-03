namespace InstantAIGate.Server.Services.Workers;

using InstantAIGate.Core.Dtos.Config;
using InstantAIGate.Core.Interfaces.Inference;
using InstantAIGate.Server.Configuration;
using InstantAIGate.Server.Hubs;
using InstantAIGate.SSR.Contracts;
using InstantAIGate.SSR.Dtos;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

public sealed class ModelStartupWorker : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly StartupModelSettings _startupSettings;
    private readonly StorageSettings _storageSettings;
    private readonly IConfiguration _configuration;
    private readonly IHubContext<GatewayHub, IGatewayHubClient> _hubContext;
    private readonly ILogger<ModelStartupWorker> _logger;

    public ModelStartupWorker(
        IServiceProvider serviceProvider,
        IOptions<StartupModelSettings> startupOptions,
        IOptions<StorageSettings> storageOptions,
        IConfiguration configuration,
        IHubContext<GatewayHub, IGatewayHubClient> hubContext,
        ILogger<ModelStartupWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _startupSettings = startupOptions.Value;
        _storageSettings = storageOptions.Value;
        _configuration = configuration;
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_startupSettings.Enabled)
        {
            _logger.LogInformation("Startup model autoload is disabled.");
            return;
        }

        if (string.IsNullOrWhiteSpace(_startupSettings.RepoId))
        {
            _logger.LogWarning("Startup model is enabled, but RepoId is empty in configuration.");
            return;
        }

        using var scope = _serviceProvider.CreateScope();
        var catalogService = scope.ServiceProvider.GetRequiredService<IModelCatalogService>();
        var modelLocator = scope.ServiceProvider.GetRequiredService<IModelLocator>();
        var downloader = scope.ServiceProvider.GetRequiredService<IModelDownloader>();
        var modelManager = scope.ServiceProvider.GetRequiredService<IModelManager>();

        var targetModel = await catalogService.FindModelByIdAsync(_startupSettings.RepoId, cancellationToken);
        if (targetModel == null)
        {
            _logger.LogError("Startup model '{RepoId}' was not found in catalog.", _startupSettings.RepoId);
            return;
        }

        bool isModelPresent = false;
        try
        {
            await modelLocator.ResolvePathsAsync(targetModel.Id, targetModel.RequiresVisionProjector, cancellationToken);
            isModelPresent = true;
        }
        catch (FileNotFoundException)
        {
            isModelPresent = false;
        }

        if (!isModelPresent)
        {
            _logger.LogInformation("Startup model '{RepoId}' is not found on disk. Initiating download...", targetModel.Id);
            var urlsToDownload = new List<string>(targetModel.DownloadUrls);
            if (targetModel.RequiresVisionProjector && targetModel.VisionProjectorUrls != null)
            {
                urlsToDownload.AddRange(targetModel.VisionProjectorUrls);
            }

            string destinationDir = Path.Combine(_storageSettings.ModelsDirectory, targetModel.Id);
            string targetModelGroup = $"download_{targetModel.Id}";

            var progress = new Progress<DownloadProgress>(async p =>
            {
                try
                {
                    var taskUser = _hubContext.Clients.Group(targetModelGroup).ReceiveDownloadProgress(p);
                    var taskAdmin = _hubContext.Clients.Group(GatewayHub.AdminGroupName).ReceiveDownloadProgress(p);
                    await Task.WhenAll(taskUser, taskAdmin);
                }
                catch (Exception ex)
                {
                    _logger.LogTrace(ex, "Failed to broadcast startup download progress.");
                }
            });

            await downloader.DownloadModelAsync(
                targetModel.Id,
                urlsToDownload,
                destinationDir,
                progress,
                cancellationToken);

            _logger.LogInformation("Model '{RepoId}' successfully downloaded to '{DestinationDir}'.", targetModel.Id, destinationDir);
        }
        else
        {
            _logger.LogInformation("Model files for '{RepoId}' already exist on disk.", targetModel.Id);
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

        _logger.LogInformation("Loading startup model '{RepoId}' into memory using profile '{Profile}'...", targetModel.Id, profileName);
        await modelManager.LoadModelAsync(config, cancellationToken);
        _logger.LogInformation("Startup model '{RepoId}' loaded successfully.", targetModel.Id);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}