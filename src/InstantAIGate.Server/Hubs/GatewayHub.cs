namespace InstantAIGate.Server.Hubs;

using InstantAIGate.Core.Dtos.Config;
using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Dtos.Session;
using InstantAIGate.Core.Dtos.Status;
using InstantAIGate.Core.Exceptions;
using InstantAIGate.Core.Interfaces.Inference;
using InstantAIGate.Server.Services.Workers;
using InstantAIGate.SSR.Contracts;
using InstantAIGate.SSR.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Channels;
using System.Threading.Tasks;

[Authorize]
public class GatewayHub : Hub<IGatewayHubClient>
{
    public const string AdminGroupName = "GatewayAdminGroup";
    private readonly ISessionInferenceManager _sessionManager;
    private readonly IInferenceEngine _inferenceEngine;
    private readonly IModelManager _modelManager;
    private readonly IModelCatalogService _catalogService;
    private readonly IQueueManager _queueManager;
    private readonly IConfiguration _configuration;
    private readonly StorageSettings _storageSettings;
    private readonly ChannelWriter<DownloadJob> _downloadChannelWriter;
    private readonly ILogger<GatewayHub> _logger;

    public GatewayHub(
        ISessionInferenceManager sessionManager,
        IInferenceEngine inferenceEngine,
        IModelManager modelManager,
        IModelCatalogService catalogService,
        IQueueManager queueManager,
        IConfiguration configuration,
        IOptions<StorageSettings> storageSettings,
        ChannelWriter<DownloadJob> downloadChannelWriter,
        ILogger<GatewayHub> logger)
    {
        _sessionManager = sessionManager;
        _inferenceEngine = inferenceEngine;
        _modelManager = modelManager;
        _catalogService = catalogService;
        _queueManager = queueManager;
        _configuration = configuration;
        _storageSettings = storageSettings.Value;
        _downloadChannelWriter = downloadChannelWriter;
        _logger = logger;
    }

    #region Connection Lifecycle
    public override async Task OnConnectedAsync()
    {
        if (Context.User?.IsInRole("Admin") == true)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, AdminGroupName);
            _logger.LogInformation("Admin connection {ConnectionId} added to {Group}", Context.ConnectionId, AdminGroupName);

            try
            {
                var metrics = _modelManager.GetMetrics();
                var nativeDetails = _modelManager.GetNativeDetails();
                await Clients.Caller.ReceiveMetrics(metrics, nativeDetails);
            }
            catch (Exception ex)
            {
                _logger.LogTrace(ex, "Failed to send initial metrics snapshot to admin caller {ConnectionId}", Context.ConnectionId);
            }
        }
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (Context.Items.TryGetValue("SessionId", out var sessionIdObj) && sessionIdObj is string sessionId)
        {
            try
            {
                await _sessionManager.ReleaseSessionAsync(sessionId, Context.ConnectionAborted);
                _logger.LogInformation("Cleaned up orphaned session {SessionId} on disconnect for connection {ConnectionId}", sessionId, Context.ConnectionId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to gracefully release session {SessionId} during disconnection", sessionId);
            }
        }
        await base.OnDisconnectedAsync(exception);
    }
    #endregion

    #region Data Plane (GatewayUser)
    [Authorize(Policy = "GatewayUser")]
    public async Task JoinSession(string sessionId, string repoId)
    {
        try
        {
            var request = new SessionStartRequest(sessionId, repoId);
            await _sessionManager.CreateSessionAsync(request, Context.ConnectionAborted);
            Context.Items["SessionId"] = sessionId;
            _logger.LogInformation("Client {ConnectionId} joined session {SessionId} for repo {RepoId}", Context.ConnectionId, sessionId, repoId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize session {SessionId}", sessionId);
            await Clients.Caller.ReceiveError(ex.Message);
        }
    }

    [Authorize(Policy = "GatewayUser")]
    public async Task LeaveSession(string sessionId)
    {
        _logger.LogInformation("Client explicitly requested release of session {SessionId}", sessionId);
        await _sessionManager.ReleaseSessionAsync(sessionId, Context.ConnectionAborted);
        Context.Items.Remove("SessionId");
        await Clients.Caller.ReceiveSessionClosed(sessionId);
    }

    [Authorize(Policy = "GatewayUser")]
    public async Task SendPromptDelta(string sessionId, ChatMessage deltaMessage)
    {
        try
        {
            await foreach (var token in _inferenceEngine.StreamDeltaGenerationAsync(
                sessionId, deltaMessage, overrideSettings: null, Context.ConnectionAborted))
            {
                await Clients.Caller.ReceiveTokenDelta(new SessionTokenDelta(sessionId, token));
            }
            await Clients.Caller.ReceiveTokenDelta(new SessionTokenDelta(sessionId, string.Empty, IsDone: true, FinishReason: "stop"));
        }
        catch (ContextOverflowException ex)
        {
            _logger.LogWarning(ex, "Context overflow in session {SessionId}", ex.SessionId);
            await Clients.Caller.ReceiveContextOverflow(ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Inference delta generation error in session {SessionId}", sessionId);
            await Clients.Caller.ReceiveError($"Inference error: {ex.Message}");
        }
    }

    [Authorize(Policy = "GatewayUser")]
    public async Task RollbackSession(string sessionId, int targetPosition)
    {
        try
        {
            await _sessionManager.RollbackToPositionAsync(sessionId, targetPosition, Context.ConnectionAborted);
            _logger.LogDebug("Session {SessionId} rolled back to position {TargetPosition}", sessionId, targetPosition);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rollback session {SessionId}", sessionId);
            await Clients.Caller.ReceiveError($"Rollback error: {ex.Message}");
        }
    }

    [Authorize(Policy = "GatewayUser")]
    public async Task ShiftSessionCache(string sessionId, int startPos, int count)
    {
        try
        {
            await _sessionManager.ShiftMemoryRangeAsync(sessionId, startPos, count, Context.ConnectionAborted);
            _logger.LogDebug("Session {SessionId} cache shifted: start={StartPos}, count={Count}", sessionId, startPos, count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to shift cache for session {SessionId}", sessionId);
            await Clients.Caller.ReceiveError($"Shift error: {ex.Message}");
        }
    }

    [Authorize(Policy = "GatewayUser")]
    public Task<int> GetSessionTokenCount(string sessionId)
    {
        return Task.FromResult(_sessionManager.GetPastTokensCount(sessionId));
    }
    #endregion

    #region User Observability (Groups)
    [Authorize(Policy = "GatewayUser")]
    public async Task SubscribeToModelDownload(string repoId)
    {
        string groupName = $"download_{repoId}";
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        _logger.LogDebug("Connection {ConnectionId} subscribed to {GroupName}", Context.ConnectionId, groupName);
    }

    [Authorize(Policy = "GatewayUser")]
    public async Task UnsubscribeFromModelDownload(string repoId)
    {
        string groupName = $"download_{repoId}";
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
        _logger.LogDebug("Connection {ConnectionId} unsubscribed from {GroupName}", Context.ConnectionId, groupName);
    }
    #endregion

    #region Control Plane (GatewayAdmin)
    [Authorize(Policy = "GatewayAdmin")]
    public Task<IEnumerable<ModelRegistryStatus>> GetModelsAsync()
    {
        return Task.FromResult(_modelManager.GetActiveModelsStatus());
    }

    [Authorize(Policy = "GatewayAdmin")]
    public Task<NativeModelDetails> GetActiveModelDetailsAsync()
    {
        return Task.FromResult(_modelManager.GetActiveModelDetails());
    }

    [Authorize(Policy = "GatewayAdmin")]
    public async Task LoadModelAsync(string repoId, string? profile)
    {
        var targetModel = await _catalogService.FindModelByIdAsync(repoId, Context.ConnectionAborted);
        if (targetModel == null)
        {
            throw new HubException($"Model '{repoId}' not found in catalog.");
        }
        var config = BuildModelSettings(targetModel, profile);
        await _modelManager.LoadModelAsync(config, Context.ConnectionAborted);
    }

    [Authorize(Policy = "GatewayAdmin")]
    public async Task SwapModelAsync(string repoId, string? profile)
    {
        var targetModel = await _catalogService.FindModelByIdAsync(repoId, Context.ConnectionAborted);
        if (targetModel == null)
        {
            throw new HubException($"Model '{repoId}' not found in catalog.");
        }
        var config = BuildModelSettings(targetModel, profile);
        await _modelManager.SwapModelAsync(config, Context.ConnectionAborted);
    }

    [Authorize(Policy = "GatewayAdmin")]
    public async Task UnloadModelAsync(string repoId)
    {
        await _modelManager.UnloadModelAsync(repoId, Context.ConnectionAborted);
    }

    [Authorize(Policy = "GatewayAdmin")]
    public async Task DownloadModelAsync(string repoId)
    {
        var model = await _catalogService.FindModelByIdAsync(repoId, Context.ConnectionAborted);
        if (model == null)
        {
            throw new HubException($"Model '{repoId}' not found in catalog.");
        }

        var downloadUrls = new List<string>(model.DownloadUrls);
        if (model.RequiresVisionProjector && model.VisionProjectorUrls != null)
        {
            downloadUrls.AddRange(model.VisionProjectorUrls);
        }

        string destinationDir = Path.Combine(_storageSettings.ModelsDirectory, model.Id);
        var job = new DownloadJob(model.Id, downloadUrls, destinationDir);
        await _downloadChannelWriter.WriteAsync(job, Context.ConnectionAborted);
    }

    [Authorize(Policy = "GatewayAdmin")]
    public Task<InferenceMetrics> GetQueueMetricsAsync()
    {
        return Task.FromResult(_modelManager.GetMetrics());
    }

    [Authorize(Policy = "GatewayAdmin")]
    public Task SetQueueLimitAsync(int limit)
    {
        if (limit <= 0)
        {
            throw new HubException("Queue limit must be greater than zero.");
        }
        _queueManager.UpdateQueueLimit(limit);
        return Task.CompletedTask;
    }
    #endregion

    #region Helpers
    private ModelSettings BuildModelSettings(CatalogModelEntry targetModel, string? profile)
    {
        var profileName = profile ?? "Default";
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
    #endregion
}