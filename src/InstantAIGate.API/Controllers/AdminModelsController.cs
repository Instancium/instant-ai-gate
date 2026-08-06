using InstantAIGate.Application.Dtos.Inference;
using InstantAIGate.Application.Interfaces.Inference;
using InstantAIGate.Application.ModelManagement.Conteracts;
using InstantAIGate.Domain.Dtos.Config;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace InstantAIGate.API.Controllers
{
    /// <summary>
    /// Extended request model containing VariantId to load a specific architecture build (e.g., CPU/CUDA).
    /// </summary>
    public class LoadModelRequest : ModelSettings
    {
        public string VariantId { get; set; } = string.Empty;
    }

    [ApiController]
    [Route("api/admin/models")]
    [Authorize(Policy = "AdminApiKeyPolicy")]
    public class AdminModelsController : ControllerBase
    {
        private readonly IModelManager _manager;
        private readonly IModelCatalog _catalog;
        private readonly ILogger<AdminModelsController> _logger;
        private readonly string _baseModelsDirectory = "models_cache";

        public AdminModelsController(
            IModelManager manager,
            IModelCatalog catalog,
            ILogger<AdminModelsController> logger)
        {
            _manager = manager;
            _catalog = catalog;
            _logger = logger;
        }

        [HttpGet]
        public IActionResult GetAllModels()
        {
            // Temporary stub returning an empty array to satisfy the UI deserializer.
            // TODO: Replace with actual catalog call when implemented:
            // var models = _catalog.GetAllModels(); 
            return Ok(Array.Empty<object>());
        }

        [HttpGet("active/telemetry")]
        public IActionResult GetActiveModelsTelemetry()
        {
            var telemetry = _manager.GetActiveModelsStatus();
            return Ok(telemetry ?? Array.Empty<ModelRegistryStatus>());
        }

        [HttpPost("load")]
        public async Task<IActionResult> LoadModelIntoMemory([FromBody] LoadModelRequest? req, CancellationToken ct)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.RepoId) || string.IsNullOrWhiteSpace(req.VariantId))
            {
                return BadRequest("Invalid request payload. Both 'repoId' and 'variantId' parameters are strictly required.");
            }

            // 1. Check against the new catalog whitelist
            var modelDefinition = _catalog.GetModel(req.RepoId);
            if (modelDefinition == null)
            {
                return NotFound($"Model with RepoId '{req.RepoId}' is not registered in the system catalog.");
            }

            var variant = modelDefinition.Variants.FirstOrDefault(v => v.VariantId.Equals(req.VariantId, StringComparison.OrdinalIgnoreCase));
            if (variant == null)
            {
                return NotFound($"Variant '{req.VariantId}' is not defined for model '{req.RepoId}'.");
            }

            // 2. Verify if the variant is downloaded to the directory structure
            string safeRepoName = req.RepoId.Replace("/", "_");
            string targetDirectory = Path.Combine(_baseModelsDirectory, safeRepoName, req.VariantId);

            if (!Directory.Exists(targetDirectory))
            {
                return StatusCode(409, new { error = "ModelNotDownloaded", message = $"The model variant '{req.VariantId}' is not downloaded to disk." });
            }

            // 3. Formulate configuration for ModelManager
            int computingThreads = req.Threads > 0 ? req.Threads : 4;
            var config = new ModelSettings
            {
                RepoId = req.RepoId,
                ModelPath = targetDirectory, // Pass directory instead of a specific file
                ContextSize = req.ContextSize,
                MaxContexts = req.MaxContexts,
                GpuLayerCount = req.GpuLayerCount,
                FlashAttention = req.FlashAttention,
                Threads = computingThreads,
                Type = req.Type,
                BatchSize = req.BatchSize,
                Embeddings = req.Embeddings,
                KvCacheQuantization = req.KvCacheQuantization,
                MaxModelFileSizeMb = req.MaxModelFileSizeMb > 0 ? req.MaxModelFileSizeMb : 4096
            };

            try
            {
                await _manager.LoadModelAsync(config, ct);
                return Ok(new { status = "loaded", repoId = req.RepoId, variantId = req.VariantId, timestamp = DateTimeOffset.UtcNow });
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

        [HttpPost("unload")]
        public async Task<IActionResult> UnloadModelFromMemory([FromBody] LoadModelRequest? req, CancellationToken ct)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.RepoId))
            {
                return BadRequest("Invalid request payload. The 'repoId' parameter is strictly required.");
            }

            try
            {
                await _manager.UnloadModelAsync(req.RepoId, ct);
                return Ok(new { status = "unloaded", repoId = req.RepoId, timestamp = DateTimeOffset.UtcNow });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "ModelEvictionFault", message = ex.Message });
            }
        }
    }
}