using InstantAIGate.Application.ModelManagement.Conteracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace InstantAIGate.API.Controllers
{
    [ApiController]
    [Route("api/admin/fetch")]
    [Authorize(Policy = "AdminApiKeyPolicy")]
    public class AdminFetchController : ControllerBase
    {
        private readonly IModelDownloadOrchestrator _orchestrator;
        private readonly IModelCatalog _catalog;
        private readonly ILogger<AdminFetchController> _logger;

        private static readonly ConcurrentDictionary<string, CancellationTokenSource> _fetchCancellations = new();
        private static readonly ConcurrentDictionary<string, double> _liveProgressRegistry = new();

        public AdminFetchController(
            IModelDownloadOrchestrator orchestrator,
            IModelCatalog catalog,
            ILogger<AdminFetchController> logger)
        {
            _orchestrator = orchestrator;
            _catalog = catalog;
            _logger = logger;
        }

        [HttpPost("start")]
        public IActionResult StartFetch([FromQuery] string repoId, [FromQuery] string variantId)
        {
            if (string.IsNullOrWhiteSpace(repoId))
            {
                return BadRequest("Parameter 'repoId' is required.");
            }
            if (string.IsNullOrWhiteSpace(variantId))
            {
                return BadRequest("Parameter 'variantId' is required.");
            }

            var model = _catalog.GetModel(repoId);
            if (model == null)
            {
                return NotFound($"Model '{repoId}' not found inside catalog.");
            }

            var variant = model.Variants.FirstOrDefault(v => v.VariantId.Equals(variantId, StringComparison.OrdinalIgnoreCase));
            if (variant == null)
            {
                return NotFound($"Variant '{variantId}' not found for model '{repoId}'.");
            }

            string fetchKey = $"{repoId}::{variantId}";

            if (_fetchCancellations.ContainsKey(fetchKey))
            {
                return Conflict("Fetch operation already running for this target reference block.");
            }

            var cts = new CancellationTokenSource();
            _fetchCancellations[fetchKey] = cts;
            _liveProgressRegistry[fetchKey] = 0.0;

            _ = Task.Run(async () =>
            {
                bool hasError = false;
                try
                {
                    await foreach (var progress in _orchestrator.ExecuteDownloadAsync(repoId, variantId, cts.Token))
                    {
                        _liveProgressRegistry[fetchKey] = progress.Percentage;
                    }

                    _liveProgressRegistry[fetchKey] = 100.0;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Background download pipeline failed for {FetchKey}", fetchKey);
                    hasError = true;
                }
                finally
                {
                    if (hasError)
                    {
                        _liveProgressRegistry[fetchKey] = -1.0;
                        await Task.Delay(3000);
                    }

                    _fetchCancellations.TryRemove(fetchKey, out _);
                    _liveProgressRegistry.TryRemove(fetchKey, out _);
                }
            });

            return Accepted(new { status = "Model pipeline fetch operation launched in background.", fetchKey });
        }

        [HttpPost("cancel")]
        public IActionResult CancelFetch([FromQuery] string fetchKey)
        {
            if (_fetchCancellations.TryRemove(fetchKey, out var cts))
            {
                cts.Cancel();
                cts.Dispose();

                _liveProgressRegistry.TryRemove(fetchKey, out _);

                return Ok(new { message = "Background target acquisition pipeline dropped." });
            }
            return NotFound("No active structural fetch process detected for this model and variant.");
        }

        [HttpPost("stream-ticket")]
        public IActionResult GetStreamTicket([FromServices] IMemoryCache cache)
        {
            var ticket = Guid.NewGuid().ToString("N");
            cache.Set(ticket, true, TimeSpan.FromSeconds(15));
            return Ok(new { ticket });
        }

        [HttpGet("progress-stream")]
        [AllowAnonymous]
        public async Task StreamProgress([FromQuery] string? ticket, [FromServices] IMemoryCache cache, CancellationToken clientCt)
        {
            if (string.IsNullOrEmpty(ticket) || !cache.TryGetValue(ticket, out _))
            {
                Response.StatusCode = 401;
                await Response.WriteAsync("Unauthorized: Invalid or expired stream ticket.", clientCt);
                return;
            }

            cache.Remove(ticket);

            Response.ContentType = "text/event-stream";
            Response.Headers.Append("Cache-Control", "no-cache");
            Response.Headers.Append("Connection", "keep-alive");

            try
            {
                while (!clientCt.IsCancellationRequested)
                {
                    var snapshot = _liveProgressRegistry.Select(kvp => new
                    {
                        fetchKey = kvp.Key,
                        progress = kvp.Value,
                        status = kvp.Value < 0 ? "error" : (kvp.Value >= 100 ? "completed" : "downloading")
                    }).ToList();

                    var payload = JsonSerializer.Serialize(snapshot);

                    await Response.WriteAsync($"data: {payload}\n\n", clientCt);
                    await Response.Body.FlushAsync(clientCt);

                    await Task.Delay(1000, clientCt);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SSE stream error occurred.");
            }
        }
    }
}