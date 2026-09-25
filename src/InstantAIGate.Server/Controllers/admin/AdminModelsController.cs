using InstantAIGate.Core.Dtos.Config;
using InstantAIGate.Core.Interfaces.Inference;
using InstantAIGate.SSR.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace InstantAIGate.Server.Controllers.admin;

[ApiController]
[Route("admin/models")]
public class AdminModelsController : ControllerBase
{
    public record DownloadModelRequest(string RepoId);

    private readonly IModelManager _modelManager;
    private readonly IModelCatalogService _catalogService;
    private readonly IConfiguration _configuration;
    private readonly IModelDownloader _downloader;
    private readonly StorageSettings _storageSettings;

    public AdminModelsController(
            IModelManager modelManager,
            IModelCatalogService catalogService,
            IConfiguration configuration,
            IModelDownloader downloader,
            Microsoft.Extensions.Options.IOptions<StorageSettings> storageOptions)
    {
        _modelManager = modelManager;
        _catalogService = catalogService;
        _configuration = configuration;
        _downloader = downloader;
        _storageSettings = storageOptions.Value;
    }

    [HttpGet]
    public async Task<IActionResult> GetModels(CancellationToken ct)
    {
        var catalog = await _catalogService.GetSupportedModelsAsync(ct);
        var activeStatus = _modelManager.GetActiveModelsStatus();

        return Ok(new { catalog, activeStatus });
    }

    [HttpPost("download")]
    public async Task<IActionResult> DownloadModel([FromBody] DownloadModelRequest request, CancellationToken ct)
    {
        var targetModel = await _catalogService.FindModelByIdAsync(request.RepoId, ct);
        if (targetModel == null)
        {
            return NotFound(new { error = $"Model '{request.RepoId}' not found in catalog." });
        }

        var urlsToDownload = new System.Collections.Generic.List<string>(targetModel.DownloadUrls);
        if (targetModel.RequiresVisionProjector && targetModel.VisionProjectorUrls != null)
        {
            urlsToDownload.AddRange(targetModel.VisionProjectorUrls);
        }

        string destinationDir = System.IO.Path.Combine(_storageSettings.ModelsDirectory, targetModel.Id);
        _ = Task.Run(async () =>
        {
            try
            {
                var progress = new System.Progress<InstantAIGate.SSR.Dtos.DownloadProgress>();
                await _downloader.DownloadModelAsync(targetModel.Id, urlsToDownload, destinationDir, progress, CancellationToken.None);
            }
            catch (System.Exception ex)
            {
            }
        });

        return Accepted(new { message = $"Download initiated for '{targetModel.Id}' into {destinationDir}." });
    }


    [HttpPost("load")]
    public async Task<IActionResult> LoadModel([FromBody] LoadModelRequest request, CancellationToken ct)
    {
        var targetModel = await _catalogService.FindModelByIdAsync(request.RepoId, ct);
        if (targetModel == null)
        {
            return NotFound(new { error = $"Model '{request.RepoId}' not found in catalog." });
        }

        var profileName = request.Profile ?? "Default";
        var hwProfile = _configuration.GetSection($"InstantAIGate:HardwareProfiles:{profileName}").Get<HardwareProfileSettings>()
                        ?? new HardwareProfileSettings();

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

        await _modelManager.LoadModelAsync(config, ct);
        return Ok(new { message = $"Model '{request.RepoId}' loaded successfully." });
    }

    [HttpPost("swap")]
    public async Task<IActionResult> SwapModel([FromBody] LoadModelRequest request, CancellationToken ct)
    {
        var targetModel = await _catalogService.FindModelByIdAsync(request.RepoId, ct);
        if (targetModel == null)
        {
            return NotFound(new { error = $"Model '{request.RepoId}' not found in catalog." });
        }

        var profileName = request.Profile ?? "Default";
        var hwProfile = _configuration.GetSection($"InstantAIGate:HardwareProfiles:{profileName}").Get<HardwareProfileSettings>()
                        ?? new HardwareProfileSettings();

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

        await _modelManager.SwapModelAsync(config, ct);
        return Ok(new { message = $"Model gracefully swapped to '{request.RepoId}'." });
    }

    [HttpPost("unload")]
    public async Task<IActionResult> UnloadModel([FromBody] UnloadModelRequest request, CancellationToken ct)
    {
        await _modelManager.UnloadModelAsync(request.RepoId, ct);
        return Ok(new { message = $"Model '{request.RepoId}' unloaded." });
    }
}

public record LoadModelRequest(string RepoId, string? Profile);
public record UnloadModelRequest(string RepoId);