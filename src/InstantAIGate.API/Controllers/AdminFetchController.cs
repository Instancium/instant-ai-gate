using InstantAIGate.Application.Interfaces.Catalog;
using InstantAIGate.Application.Interfaces.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using System.Collections.Concurrent;
using System.Text.Json;

namespace InstantAIGate.API.Controllers
{
    [ApiController]
    [Route("api/admin/fetch")]
    [Authorize(Policy = "AdminApiKeyPolicy")] 
    public class AdminFetchController : ControllerBase
    {
        private readonly IModelRegistry _modelRegistry;
        private readonly IModelStorageService _storageService;
        private readonly ILogger<AdminFetchController> _logger;

        private static readonly ConcurrentDictionary<string, CancellationTokenSource> _fetchCancellations = new();
        private static readonly ConcurrentDictionary<string, double> _liveProgressRegistry = new();
        private static readonly ConcurrentDictionary<string, string> _currentRunningFiles = new();

        public AdminFetchController(IModelRegistry modelRegistry,
            IModelStorageService storageService,
            ILogger<AdminFetchController> logger)
        {
            _modelRegistry = modelRegistry;
            _storageService = storageService;
            _logger = logger;
        }

        [HttpPost("start")]
        public async Task<IActionResult> StartFetch([FromQuery] string repoId)
        {
            if (string.IsNullOrWhiteSpace(repoId)) return BadRequest("Parameter 'repoId' is required.");

            var model = await _modelRegistry.GetModelAsync(repoId);
            if (model == null) return NotFound($"Model '{repoId}' not found inside catalog.");

            if (_fetchCancellations.ContainsKey(repoId))
            {
                return Conflict("Fetch operation already running for this target reference block.");
            }

            var cts = new CancellationTokenSource();
            _fetchCancellations[repoId] = cts;
            _liveProgressRegistry[repoId] = 0.0;

            _ = Task.Run(async () =>
            {
                bool hasError = false;
                try
                {
                    long totalModelBytes = model.Files.Sum(f => f.SizeBytes > 0 ? f.SizeBytes : 1);
                    long completedBytes = 0;

                    foreach (var file in model.Files)
                    {
                        if (cts.Token.IsCancellationRequested) break;
                        _currentRunningFiles[repoId] = file.FileName;

                        _liveProgressRegistry[repoId] = (double)completedBytes / totalModelBytes * 100.0;

                        await foreach (var progress in _storageService.DownloadModelFileAsync(
                            file.Url, model.RepoId, file.FileName, file.SizeBytes, cts.Token))
                        {
                            long overallDownloaded = completedBytes + progress.BytesDownloaded;
                            double percentage = (double)overallDownloaded / totalModelBytes * 100.0;

                            if (percentage >= 100.0 && file != model.Files.Last())
                            {
                                percentage = 99.9;
                            }

                            _liveProgressRegistry[repoId] = percentage;
                        }

                        completedBytes += file.SizeBytes;
                    }

                    _liveProgressRegistry[repoId] = 100.0;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Background download pipeline failed for model {RepoId}", repoId);
                    hasError = true;
                }
                finally
                {
                    if (hasError)
                    {
                        _liveProgressRegistry[repoId] = -1.0;
                        await Task.Delay(3000);
                    }

                    _fetchCancellations.TryRemove(repoId, out _);
                    _liveProgressRegistry.TryRemove(repoId, out _);
                    _currentRunningFiles.TryRemove(repoId, out _);
                }
            });

            return Accepted(new { status = "Model pipeline fetch operation launched in background." });
        }

        [HttpPost("cancel")]
        public IActionResult CancelFetch([FromQuery] string repoId)
        {
            if (_fetchCancellations.TryRemove(repoId, out var cts))
            {
                cts.Cancel();
                cts.Dispose();

                _liveProgressRegistry.TryRemove(repoId, out _);
                _currentRunningFiles.TryRemove(repoId, out _);

                return Ok(new { message = "Background target acquisition pipeline dropped." });
            }
            return NotFound("No active structural fetch process detected for this model.");
        }

        /// <summary>
        /// Issues a one-time, short-lived ticket for connection to SSE.
        /// This endpoint inherits the controller-level [Authorize] attribute.
        /// </summary>
        [HttpPost("stream-ticket")]
        public IActionResult GetStreamTicket([FromServices] IMemoryCache cache)
        {
            var ticket = Guid.NewGuid().ToString("N");
            // The ticket lasts only 15 seconds—just long enough to open EventSource
            cache.Set(ticket, true, TimeSpan.FromSeconds(15));
            return Ok(new { ticket });
        }

        /// <summary>
        /// Long-lived continuous network feed reporting storage layout progress streams.
        /// Allowed anonymously, but strictly validated via the short-lived ticket.
        /// </summary>
        [HttpGet("progress-stream")]
        [AllowAnonymous] 
        public async Task StreamProgress([FromQuery] string? ticket, [FromServices] IMemoryCache cache, CancellationToken clientCt)
        {
            // Strictly validate and consume the 15-second ticket
            if (string.IsNullOrEmpty(ticket) || !cache.TryGetValue(ticket, out _))
            {
                Response.StatusCode = 401;
                await Response.WriteAsync("Unauthorized: Invalid or expired stream ticket.");
                return;
            }

            // Burn the ticket so it cannot be reused
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
                        repoId = kvp.Key,
                        progress = kvp.Value,
                        currentFile = _currentRunningFiles.TryGetValue(kvp.Key, out var f) ? f : string.Empty,
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
                // Expected when user navigates away
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SSE stream error occurred.");
            }
        }
    }
}