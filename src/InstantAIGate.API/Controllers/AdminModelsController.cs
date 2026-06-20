using InstantAIGate.Application.Interfaces.Catalog;
using InstantAIGate.Application.Interfaces.Inference;
using InstantAIGate.Application.Interfaces.Storage;
using InstantAIGate.Domain.Dtos.Config;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace InstantAIGate.API.Controllers
{
    /// <summary>
    /// Administrative Control Plane API providing orchestration endpoints for model lifecycle operations,
    /// including catalog discovery, binary storage streaming, memory allocation, and telemetry.
    /// </summary>
    [ApiController]
    [Route("api/admin/models")] // Refactored to represent a unified management plane
    [Authorize(Policy = "AdminApiKeyPolicy")]
    public class AdminModelsController(
        IModelManager manager,
        IModelRegistry modelRegistry,
        IModelStorageChecker checker,
        IModelStorageService storageService,
        IModelPathProvider pathProvider) : ControllerBase
    {

        /// <summary>
        /// Retrieves the entire system model catalog populated with download metadata and local disk resolution layouts.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetAllModels()
        {
            var models = await modelRegistry.GetAllModelsAsync();

            var result = models.Select(m =>
            {
                // Dynamic resolution fallback: resolve size directly from storage blocks if baseline tracking is zero
                long resolvedBytes = m.TotalSizeBytes;

                if (resolvedBytes <= 0)
                {
                    string targetFolder = pathProvider.GetModelDirectory(m.RepoId);
                    foreach (var file in m.Files)
                    {
                        string absoluteFilePath = Path.Combine(targetFolder, file.FileName);
                        if (System.IO.File.Exists(absoluteFilePath))
                        {
                            resolvedBytes += new FileInfo(absoluteFilePath).Length;
                        }
                    }
                }

                return new
                {
                    m.RepoId,
                    m.DisplayName,
                    SizeBytes = resolvedBytes, // CRITICAL FIX: Named explicitly to bind with frontend ModelViewItem tracking
                    IsDownloaded = checker.IsModelDownloaded(m),
                    PrimaryLocalPath = checker.GetModelPath(m),
                    Type = m.Type.ToString(),
                    Files = m.Files.Select(f => new
                    {
                        f.FileName,
                        f.Url,
                        f.SizeBytes
                    }).ToList()
                };
            });

            return Ok(result);
        }

        /// <summary>
        /// Initiates sequential asynchronous binary stream downloads for all shards belonging to a model,
        /// pushing localized chunk progress updates down a Server-Sent Events (SSE) pipe.
        /// </summary>
        [HttpGet("download")]
        public async Task DownloadModel([FromQuery] string repoId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(repoId))
            {
                Response.StatusCode = 400;
                await Response.WriteAsync("The 'repoId' query parameter is required.", ct);
                return;
            }

            var model = await modelRegistry.GetModelAsync(repoId);
            if (model == null)
            {
                Response.StatusCode = 404;
                await Response.WriteAsync($"Model '{repoId}' was not found in the system catalog registry.", ct);
                return;
            }

            Response.ContentType = "text/event-stream";
            Response.Headers.Append("Cache-Control", "no-cache");
            Response.Headers.Append("Connection", "keep-alive");

            try
            {
                // Iterate through every single registered split file shard inside the metadata profile
                foreach (var file in model.Files)
                {
                    await foreach (var progress in storageService.DownloadModelFileAsync(
                        file.Url,
                        model.RepoId,
                        file.FileName,
                        file.SizeBytes,
                        ct))
                    {
                        // Enriched payload structure to inform dashboard UI exactly which shard is progressing
                        var eventData = new
                        {
                            currentFile = file.FileName,
                            totalFilesCount = model.Files.Count,
                            progressDetails = progress
                        };

                        var json = JsonSerializer.Serialize(eventData);
                        await Response.WriteAsync($"data: {json}\n\n", ct);
                        await Response.Body.FlushAsync(ct);
                    }
                }
            }
            catch (Exception ex)
            {
                // Gracefully catch background disruptions on streaming I/O loops
                var errorPayload = JsonSerializer.Serialize(new { error = "StreamInterrupted", message = ex.Message });
                await Response.WriteAsync($"data: {errorPayload}\n\n", ct);
            }
        }

        /// <summary>
        /// Retrieves runtime tracking state and memory metrics for all models currently active in the execution pool.
        /// </summary>
        [HttpGet("active/telemetry")]
        public IActionResult GetActiveModelsTelemetry()
        {
            var telemetry = manager.GetActiveModelsStatus();
            return Ok(telemetry ?? []);
        }


        /// <summary>
        /// Instantiates and allocates execution nodes for a downloaded model within the native system runtime.
        /// </summary>
        [HttpPost("load")]
        public async Task<IActionResult> LoadModelIntoMemory([FromBody] ModelSettings? req, CancellationToken ct)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.RepoId))
            {
                return BadRequest("Invalid request payload. The 'repoId' parameter is strictly required.");
            }

            var model = await modelRegistry.GetModelAsync(req.RepoId);
            if (model is null)
            {
                return NotFound($"Model with RepoId '{req.RepoId}' is not registered in the system metadata repository.");
            }

            if (!checker.IsModelDownloaded(model))
            {
                return StatusCode(409, new { error = "ModelNotDownloaded", message = "All the model binary shards must be downloaded to disk prior to memory loading execution." });
            }

            // Resolves the primary binary chunk path (-00001) for the native llama.cpp loading pipeline
            var fullPhysicalPath = await pathProvider.GetFullModelPathAsync(req.RepoId);
            int computingThreads = req.Threads > 0 ? req.Threads : 4;
            var config = new ModelSettings
            {
                RepoId = model.RepoId,
                ModelPath = fullPhysicalPath,
                ContextSize = req.ContextSize,
                MaxContexts = req.MaxContexts,
                GpuLayerCount = req.GpuLayerCount,
                FlashAttention = req.FlashAttention,
                Threads = computingThreads,
                Type = model.Type,
                BatchSize = req.BatchSize,
                Embeddings = req.Embeddings,
                KvCacheQuantization = req.KvCacheQuantization,
                // If caller didn't provide MaxModelFileSizeMb, infer from registered model file sizes on disk
                MaxModelFileSizeMb = 0
            };

            if (req is not null && (req is { } ))
            {
                // try to compute default size from metadata
                long totalBytes = 0;
                foreach (var f in model.Files)
                {
                    if (f.SizeBytes > 0) totalBytes += f.SizeBytes;
                    else
                    {
                        // fallback to local disk resolution
                        var folder = pathProvider.GetModelDirectory(model.RepoId);
                        var absolute = Path.Combine(folder, f.FileName);
                        if (System.IO.File.Exists(absolute)) totalBytes += new FileInfo(absolute).Length;
                    }
                }

                //int inferredMb = (int)Math.Ceiling(totalBytes / (1024.0 * 1024.0));
                int inferredMb = (int)Math.Ceiling(totalBytes / 1_000_000.0);
                if (inferredMb <= 0) inferredMb = 4096; // fallback default

                // If request contains MaxModelFileSizeMb (new property not present in the record), prefer it; otherwise use inferred
                // Note: older clients won't send this field; we therefore default to inferredMb
                config.MaxModelFileSizeMb = inferredMb;
            }

            try
            {
                await manager.LoadModelAsync(config, ct);
                return Ok(new { status = "loaded", repoId = req?.RepoId, timestamp = DateTimeOffset.UtcNow });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = "ModelLoadError", message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "NativeInfrastructureFault", message = ex.Message });
            }
        }

        /// <summary>
        /// De-allocates execution instances and drops RAM/VRAM resource pools reserved for the specified active model.
        /// </summary>
        [HttpPost("unload")]
        public async Task<IActionResult> UnloadModelFromMemory([FromBody] ModelSettings? req, CancellationToken ct)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.RepoId))
            {
                return BadRequest("Invalid request payload. The 'repoId' parameter is strictly required.");
            }

            try
            {
                await manager.UnloadModelAsync(req.RepoId, ct);
                return Ok(new { status = "unloaded", repoId = req.RepoId, timestamp = DateTimeOffset.UtcNow });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "ModelEvictionFault", message = ex.Message });
            }
        }
    }
}