namespace InstantAIGate.Server.Services.Workers;

using InstantAIGate.Server.Hubs;
using InstantAIGate.SSR.Contracts;
using InstantAIGate.SSR.Dtos;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

public record DownloadJob(string ModelId, IReadOnlyList<string> Urls, string DestinationDir);

public class ModelDownloadWorker : BackgroundService
{
    private readonly ChannelReader<DownloadJob> _queueReader;
    private readonly IServiceProvider _serviceProvider;
    private readonly IHubContext<GatewayHub, IGatewayHubClient> _hubContext;
    private readonly ILogger<ModelDownloadWorker> _logger;

    public ModelDownloadWorker(
        ChannelReader<DownloadJob> queueReader,
        IServiceProvider serviceProvider,
        IHubContext<GatewayHub, IGatewayHubClient> hubContext,
        ILogger<ModelDownloadWorker> logger)
    {
        _queueReader = queueReader;
        _serviceProvider = serviceProvider;
        _hubContext = hubContext;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in _queueReader.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var downloader = scope.ServiceProvider.GetRequiredService<IModelDownloader>();

                string targetModelGroup = $"download_{job.ModelId}";

                var progress = new Progress<DownloadProgress>(async progressData =>
                {
                    try
                    {
                        var taskUser = _hubContext.Clients.Group(targetModelGroup)
                            .ReceiveDownloadProgress(progressData);
                        var taskAdmin = _hubContext.Clients.Group(GatewayHub.AdminGroupName)
                            .ReceiveDownloadProgress(progressData);

                        await Task.WhenAll(taskUser, taskAdmin);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogTrace(ex, "Failed to broadcast download progress for {ModelId}.", job.ModelId);
                    }
                });

                _logger.LogInformation("Starting background download for {ModelId}...", job.ModelId);
                await downloader.DownloadModelAsync(
                    job.ModelId,
                    job.Urls,
                    job.DestinationDir,
                    progress,
                    stoppingToken);

                _logger.LogInformation("Download completed successfully for {ModelId}.", job.ModelId);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Download for {ModelId} was gracefully cancelled by host shutdown.", job.ModelId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Background download failed for {ModelId}.", job.ModelId);
            }
        }
    }
}