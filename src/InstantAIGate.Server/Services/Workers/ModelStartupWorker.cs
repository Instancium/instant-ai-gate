namespace InstantAIGate.Server.Services.Workers;

using InstantAIGate.Core.Dtos.Config;
using InstantAIGate.Core.Dtos.Status;
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
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

public sealed class ModelStartupWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly StartupModelSettings _startupSettings;
    private readonly StorageSettings _storageSettings;
    private readonly IConfiguration _configuration;
    private readonly IHubContext<GatewayHub, IGatewayHubClient> _hubContext;
    private readonly IGatewayStateManager _stateManager;
    private readonly ILogger<ModelStartupWorker> _logger;

    public ModelStartupWorker(
        IServiceProvider serviceProvider,
        IOptions<StartupModelSettings> startupOptions,
        IOptions<StorageSettings> storageOptions,
        IConfiguration configuration,
        IHubContext<GatewayHub, IGatewayHubClient> hubContext,
        IGatewayStateManager stateManager,
        ILogger<ModelStartupWorker> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _startupSettings = startupOptions?.Value ?? throw new ArgumentNullException(nameof(startupOptions));
        _storageSettings = storageOptions?.Value ?? throw new ArgumentNullException(nameof(storageOptions));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
        _stateManager = stateManager ?? throw new ArgumentNullException(nameof(stateManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Yield execution immediately so Generic Host finishes startup and binds Kestrel listeners
        await Task.Yield();

        if (!_startupSettings.Enabled)
        {
            _logger.LogInformation("Startup model autoload is disabled.");
            return;
        }

        if (string.IsNullOrWhiteSpace(_startupSettings.RepoId))
        {
            _logger.LogWarning("Startup model autoload is enabled, but RepoId is empty in configuration.");
            return;
        }

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var catalogService = scope.ServiceProvider.GetRequiredService<IModelCatalogService>();
            var modelLocator = scope.ServiceProvider.GetRequiredService<IModelLocator>();
            var downloader = scope.ServiceProvider.GetRequiredService<IModelDownloader>();
            var modelManager = scope.ServiceProvider.GetRequiredService<IModelManager>();

            _logger.LogInformation("Querying catalog for startup model '{RepoId}'...", _startupSettings.RepoId);
            var targetModel = await catalogService.FindModelByIdAsync(_startupSettings.RepoId, stoppingToken);
            if (targetModel == null)
            {
                var notFoundMessage = $"Startup model '{_startupSettings.RepoId}' was not found in catalog.";
                _logger.LogError("{ErrorMessage}", notFoundMessage);
                _stateManager.SetFaulted(_startupSettings.RepoId, notFoundMessage);
                return;
            }

            bool isModelPresent = false;
            try
            {
                await modelLocator.ResolvePathsAsync(targetModel.Id, targetModel.RequiresVisionProjector, stoppingToken);
                isModelPresent = true;
            }
            catch (FileNotFoundException)
            {
                isModelPresent = false;
            }

            if (!isModelPresent)
            {
                _logger.LogInformation("Startup model '{RepoId}' is not found on disk. Initiating download pipeline...", targetModel.Id);

                var urlsToDownload = new List<string>(targetModel.DownloadUrls);
                if (targetModel.RequiresVisionProjector && targetModel.VisionProjectorUrls != null)
                {
                    urlsToDownload.AddRange(targetModel.VisionProjectorUrls);
                }

                string destinationDir = Path.Combine(_storageSettings.ModelsDirectory, targetModel.Id);
                string targetModelGroup = $"download_{targetModel.Id}";

                _stateManager.SetDownloading(
                    targetModel.Id,
                    progressPercentage: 0f,
                    downloadedBytes: 0,
                    totalBytes: (long)targetModel.TotalFileSizeBytes,
                    bytesPerSecond: 0.0,
                    stageDescription: $"Downloading startup model '{targetModel.Id}'");

                long lastLogTimestamp = 0;
                var stopwatch = Stopwatch.StartNew();

                var progress = new Progress<DownloadProgress>(async progressData =>
                {
                    _stateManager.SetDownloading(
                        progressData.ModelId,
                        progressData.Percentage,
                        progressData.BytesDownloaded,
                        progressData.TotalBytes,
                        progressData.SpeedBytesPerSecond,
                        $"Downloading '{progressData.ModelId}' ({progressData.Percentage:F1}%)");

                    long elapsedMs = stopwatch.ElapsedMilliseconds;
                    if (elapsedMs - lastLogTimestamp >= 2000 || Math.Abs(progressData.Percentage - 100.0f) < 0.01f)
                    {
                        lastLogTimestamp = elapsedMs;
                        double downloadedMb = progressData.BytesDownloaded / (1024.0 * 1024.0);
                        double totalMb = progressData.TotalBytes / (1024.0 * 1024.0);
                        double speedMb = progressData.SpeedBytesPerSecond / (1024.0 * 1024.0);

                        _logger.LogInformation(
                            "[Download: {ModelId}] {Percentage:F1}% | {DownloadedMB:F1}/{TotalMB:F1} MB | {Speed:F2} MB/s",
                            progressData.ModelId,
                            progressData.Percentage,
                            downloadedMb,
                            totalMb,
                            speedMb);
                    }

                    try
                    {
                        var taskUser = _hubContext.Clients.Group(targetModelGroup).ReceiveDownloadProgress(progressData);
                        var taskAdmin = _hubContext.Clients.Group(GatewayHub.AdminGroupName).ReceiveDownloadProgress(progressData);
                        await Task.WhenAll(taskUser, taskAdmin);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogTrace(ex, "Failed to broadcast download progress for '{ModelId}'.", targetModel.Id);
                    }
                });

                await downloader.DownloadModelAsync(
                    targetModel.Id,
                    urlsToDownload,
                    destinationDir,
                    progress,
                    stoppingToken);

                _logger.LogInformation("Startup model '{RepoId}' successfully downloaded.", targetModel.Id);
            }

            _stateManager.SetLoading(targetModel.Id, $"Loading model '{targetModel.Id}' into runtime memory");
            _logger.LogInformation("Loading startup model '{RepoId}' into memory...", targetModel.Id);

            var modelSettings = BuildStartupModelSettings(targetModel);
            await modelManager.LoadModelAsync(modelSettings, stoppingToken);

            _stateManager.SetReady(targetModel.Id, $"Startup model '{targetModel.Id}' is loaded and ready for inference");
            _logger.LogInformation("Startup model '{RepoId}' successfully loaded and ready for inference.", targetModel.Id);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Model startup worker was cancelled during host shutdown.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error during startup model initialization for '{RepoId}'.", _startupSettings.RepoId);
            _stateManager.SetFaulted(_startupSettings.RepoId, "Fatal error during startup model initialization", ex);
        }
    }

    private ModelSettings BuildStartupModelSettings(CatalogModelEntry targetModel)
    {
        var profileName = string.IsNullOrWhiteSpace(_startupSettings.Profile) ? "Default" : _startupSettings.Profile;
        var hwProfile = _configuration.GetSection($"InstantAIGate:HardwareProfiles:{profileName}").Get<HardwareProfileSettings>() ?? new HardwareProfileSettings();

        return new ModelSettings
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
    }
}