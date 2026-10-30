namespace InstantAIGate.Server.Hubs;

using InstantAIGate.Core.Dtos.Config;
using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Dtos.Session;
using InstantAIGate.Core.Dtos.Status;
using InstantAIGate.Core.Exceptions;
using InstantAIGate.Core.Interfaces.Inference;
using InstantAIGate.Server.Configuration;
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
using System.Globalization;
using System.IO;
using System.Linq;
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
    private readonly IGatewayStateManager _stateManager;
    private readonly IEmbeddingModelResolver _embeddingResolver;
    private readonly HardwareProfilePolicy _profilePolicy;
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
        IGatewayStateManager stateManager,
        IEmbeddingModelResolver embeddingResolver,
        HardwareProfilePolicy profilePolicy,
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
        _stateManager = stateManager;
        _embeddingResolver = embeddingResolver ?? throw new ArgumentNullException(nameof(embeddingResolver));
        _profilePolicy = profilePolicy ?? throw new ArgumentNullException(nameof(profilePolicy));
        _logger = logger;
    }


    [Authorize(Policy = "GatewayUser")]
    public async Task<EmbeddingResponse> GenerateEmbeddingsAsync(string repoId, EmbeddingRequest request)
    {
        string targetRepoId;
        try
        {
            targetRepoId = _embeddingResolver.Resolve(repoId);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("Embedding target resolution rejected for '{RepoId}': {Message}", repoId, ex.Message);
            throw new HubException(ex.Message);
        }

        try
        {
            var response = await _inferenceEngine.GenerateEmbeddingsAsync(targetRepoId, request, Context.ConnectionAborted);
            _logger.LogInformation("Generated embeddings for model {ModelId}: {TokenCount} tokens, {Dim}D vector",
                targetRepoId, response.TokenCount, response.EmbeddingDimension);
            return response;
        }
        catch (ContextOverflowException ex)
        {
            _logger.LogWarning(ex, "Context overflow during embedding generation for model {ModelId}", targetRepoId);
            throw new HubException($"Input text exceeds model context size: {ex.Message}");
        }
        catch (HubException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate embeddings for model {ModelId}", targetRepoId);
            throw new HubException($"Embedding generation failed: {ex.Message}");
        }
    }

    [Authorize(Policy = "GatewayUser")]
    public async Task<int> GetEmbeddingDimensionAsync(string repoId)
    {
        string targetRepoId;
        try
        {
            targetRepoId = _embeddingResolver.Resolve(repoId);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("Embedding target resolution rejected for '{RepoId}': {Message}", repoId, ex.Message);
            throw new HubException(ex.Message);
        }

        try
        {
            int dimension = await _inferenceEngine.GetEmbeddingDimensionAsync(targetRepoId, Context.ConnectionAborted);
            _logger.LogInformation("Embedding dimension for model {ModelId}: {Dimension}", targetRepoId, dimension);
            return dimension;
        }
        catch (HubException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get embedding dimension for model {ModelId}", targetRepoId);
            throw new HubException($"Failed to retrieve embedding dimension: {ex.Message}");
        }
    }



















































































































































































































































































































































































































































































































































































    #region Connection Lifecycle

    public override async Task OnConnectedAsync()
    {
        try
        {
            var statusSnapshot = _stateManager.GetSnapshot();
            await Clients.Caller.ReceiveGatewayStatus(statusSnapshot);
        }
        catch (Exception ex)
        {
            _logger.LogTrace(ex, "Failed to send initial status snapshot to caller {ConnectionId}", Context.ConnectionId);
        }

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
        // ARM GRACE PERIOD: When a connection drops, inform SessionInferenceManager.
        // It starts a configurable grace period timer (default 30s) for all sessions bound to this connection.
        // If client reconnects within grace period, KV-cache is preserved seamlessly.
        // If client does not reconnect, the session is safely evicted.
        _sessionManager.NotifyConnectionDisconnected(Context.ConnectionId);

        _logger.LogInformation(
            "Client {ConnectionId} disconnected. Grace period started for its bound sessions.",
            Context.ConnectionId);

        await base.OnDisconnectedAsync(exception);
    }

    #endregion

    #region Data Plane (GatewayUser)

    [Authorize(Policy = "GatewayUser")]
    public async Task JoinSession(string sessionId, string repoId)
    {
        try
        {
            var snapshot = _stateManager.GetSnapshot();

            if (snapshot.Status == GatewayOperationalStatus.ModelDownloading)
            {
                string progressStr = snapshot.ProgressPercentage.ToString("F1", CultureInfo.InvariantCulture);
                await Clients.Caller.ReceiveError(
                    $"Server is downloading startup model '{snapshot.ActiveModelId}' ({progressStr}%). Please wait.");
                return;
            }

            if (snapshot.Status == GatewayOperationalStatus.ModelLoading)
            {
                await Clients.Caller.ReceiveError(
                    $"Server is loading startup model '{snapshot.ActiveModelId}' into memory. Please wait.");
                return;
            }

            if (snapshot.Status == GatewayOperationalStatus.Faulted)
            {
                await Clients.Caller.ReceiveError($"Server startup failed: {snapshot.ErrorMessage}");
                return;
            }

            var activeModels = _modelManager.GetActiveModels().ToList();

            if (string.IsNullOrWhiteSpace(repoId))
            {
                var defaultSettings = _modelManager.GetActiveSettings();
                if (defaultSettings == null || string.IsNullOrWhiteSpace(defaultSettings.RepoId))
                {
                    await Clients.Caller.ReceiveError(
                        "No active model is loaded on Remote Gateway. Server has no model in memory.");
                    return;
                }
                repoId = defaultSettings.RepoId;
            }
            else
            {
                var targetSettings = _modelManager.GetModelSettings(repoId) ?? _modelManager.GetActiveSettings(repoId);
                if (targetSettings == null || !string.Equals(targetSettings.RepoId, repoId, StringComparison.OrdinalIgnoreCase))
                {
                    await Clients.Caller.ReceiveError(
                        $"Requested model '{repoId}' is not active on Remote Gateway.");
                    return;
                }
            }

            var request = new SessionStartRequest(sessionId, repoId);
            await _sessionManager.CreateSessionAsync(request, Context.ConnectionId, Context.ConnectionAborted);
            _logger.LogInformation(
                "Client {ConnectionId} joined session {SessionId} for repo {RepoId}",
                Context.ConnectionId, sessionId, repoId);
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

        _sessionManager.UnbindConnection(sessionId);
        await _sessionManager.ReleaseSessionAsync(sessionId, destroySlot: false, Context.ConnectionAborted);
        await Clients.Caller.ReceiveSessionClosed(sessionId);
    }

    [Authorize(Policy = "GatewayUser")]
    public async Task DestroySession(string sessionId)
    {
        _logger.LogInformation("Client explicitly requested DESTRUCTION of session slot {SessionId}", sessionId);

        _sessionManager.UnbindConnection(sessionId);
        await _sessionManager.ReleaseSessionAsync(sessionId, destroySlot: true, Context.ConnectionAborted);
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
            await Clients.Caller.ReceiveContextOverflow(ContextOverflowReport.FromException(ex));
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

    #region User Observability & Gateway Status

    [Authorize(Policy = "GatewayUser")]
    public Task<GatewayStatusDetails> GetGatewayStatus()
    {
        return Task.FromResult(_stateManager.GetSnapshot());
    }

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
    public async Task PurgeIdleContextsAsync(string repoId)
    {
        _logger.LogInformation("Admin requested idle context purge for repo {RepoId}", repoId);
        await _modelManager.PurgeIdleContextsAsync(repoId, Context.ConnectionAborted);
    }

    [Authorize(Policy = "GatewayAdmin")]
    public Task<IEnumerable<ModelRegistryStatus>> GetModelsAsync()
    {
        return Task.FromResult(_modelManager.GetActiveModelsStatus());
    }

    [Authorize(Policy = "GatewayUser")]
    public Task<NativeModelDetails> GetActiveModelDetailsAsync()
    {
        return Task.FromResult(_modelManager.GetActiveModelDetails());
    }

    [Authorize(Policy = "GatewayUser")]
    public Task<IEnumerable<NativeModelDetails>> GetLoadedModelsDetailsAsync()
    {
        return Task.FromResult(_modelManager.GetNativeDetails());
    }

    [Authorize(Policy = "GatewayAdmin")]
    public async Task LoadModelAsync(string repoId, string? profileName = null)
    {
        var targetModel = await _catalogService.FindModelByIdAsync(repoId, Context.ConnectionAborted);
        bool isEmbedding = HardwareProfilePolicy.IsEmbeddingModel(repoId, targetModel?.Architecture);
        string profile = _profilePolicy.ResolveProfileName(profileName, isEmbedding);
        HardwareProfileSettings hwProfile = _profilePolicy.ResolveProfile(profile);
        bool visionSupport = targetModel?.RequiresVisionProjector ?? false;
        bool embeddingsEnabled = hwProfile.Embeddings || isEmbedding;

        var settings = new ModelSettings
        {
            RepoId = repoId,
            VisionSupport = visionSupport,
            Type = visionSupport ? ModelType.Vlm : (embeddingsEnabled ? ModelType.Embedding : ModelType.Llm),
            GpuLayerCount = hwProfile.GpuLayerCount,
            MainGPU = hwProfile.MainGPU,
            ContextSize = hwProfile.ContextSize,
            BatchSize = hwProfile.BatchSize,
            Threads = hwProfile.Threads,
            FlashAttention = hwProfile.FlashAttention,
            Embeddings = embeddingsEnabled,
            KvCacheQuantization = hwProfile.KvCacheQuantization,
            UseMemoryLock = hwProfile.UseMemoryLock,
            MaxContexts = hwProfile.MaxContexts
        };

        _stateManager.SetLoading(repoId, $"Loading model '{repoId}'");
        try
        {
            await _modelManager.LoadModelAsync(settings, Context.ConnectionAborted);
            string primaryRepoId = _modelManager.GetActiveSettings()?.RepoId ?? repoId;
            _stateManager.SetReady(primaryRepoId);
            _logger.LogInformation("Model {RepoId} loaded with profile {Profile} (Embeddings={Embeddings}). Primary: {Primary}",
                repoId, profile, settings.Embeddings, primaryRepoId);
        }
        catch (Exception ex)
        {
            _stateManager.SetFaulted(repoId, $"Failed to load model '{repoId}'", ex);
            throw;
        }
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
        _stateManager.SetLoading(repoId, $"Swapping to model '{repoId}'");
        try
        {
            await _modelManager.SwapModelAsync(config, Context.ConnectionAborted);
            string primaryRepoId = _modelManager.GetActiveSettings()?.RepoId ?? repoId;
            _stateManager.SetReady(primaryRepoId);
        }
        catch (Exception ex)
        {
            _stateManager.SetFaulted(repoId, $"Failed to swap to model '{repoId}'", ex);
            throw;
        }
    }

    [Authorize(Policy = "GatewayAdmin")]
    public async Task UnloadModelAsync(string repoId)
    {
        await _modelManager.UnloadModelAsync(repoId, Context.ConnectionAborted);
        var remaining = _modelManager.GetActiveModels().ToList();
        if (remaining.Count == 0)
        {
            _stateManager.Reset();
        }
        else
        {
            string primaryRepoId = _modelManager.GetActiveSettings()?.RepoId ?? remaining[0];
            _stateManager.SetReady(primaryRepoId);
        }
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
        bool isEmbedding = HardwareProfilePolicy.IsEmbeddingModel(targetModel.Id, targetModel.Architecture);
        string profileName = _profilePolicy.ResolveProfileName(profile, isEmbedding);
        HardwareProfileSettings hwProfile = _profilePolicy.ResolveProfile(profileName);

        return new ModelSettings
        {
            RepoId = targetModel.Id,
            VisionSupport = targetModel.RequiresVisionProjector,
            Type = targetModel.RequiresVisionProjector ? ModelType.Vlm : (hwProfile.Embeddings || isEmbedding ? ModelType.Embedding : ModelType.Llm),
            GpuLayerCount = hwProfile.GpuLayerCount,
            MainGPU = hwProfile.MainGPU,
            ContextSize = hwProfile.ContextSize,
            BatchSize = hwProfile.BatchSize,
            Threads = hwProfile.Threads,
            FlashAttention = hwProfile.FlashAttention,
            Embeddings = hwProfile.Embeddings || isEmbedding,
            KvCacheQuantization = hwProfile.KvCacheQuantization,
            UseMemoryLock = hwProfile.UseMemoryLock,
            MaxContexts = hwProfile.MaxContexts
        };
    }

    #endregion
}