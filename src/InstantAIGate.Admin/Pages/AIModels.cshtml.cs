using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using System.Text.Json;
using InstantAIGate.Admin.Config;
using InstantAIGate.Admin.Dtos;
using InstantAIGate.Application.Config;
using InstantAIGate.Application.Dtos.Inference;
using InstantAIGate.Domain.Dtos.Config;

namespace InstantAIGate.Admin.Pages
{
    public class AIModelsModel : PageModel
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IOptions<APIClientOptions> _apiOptions;
        private readonly GatewayConfig _gatewayConfig;
        private readonly ILogger<AIModelsModel> _logger;

        public string APIUrl { get; set; }
        public List<ModelViewItem> Models { get; set; } = new();

        public HashSet<string> ActiveModelRepoIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, ModelRegistryStatus> ActiveModelsTelemetry { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        [BindProperty]
        public ModelSettings ModelSettings { get; set; } = new();

        public AIModelsModel(
            IHttpClientFactory httpClientFactory,
            IOptions<APIClientOptions> apiOptions,
            GatewayConfig gatewayConfig,
            ILogger<AIModelsModel> logger)
        {
            _httpClientFactory = httpClientFactory;
            _apiOptions = apiOptions;
            _gatewayConfig = gatewayConfig;
            _logger = logger;
            APIUrl = _apiOptions.Value.PublicUrl;
        }

        public async Task<IActionResult> OnGetAsync()
        {
            await LoadModelsDataAsync();
            return Page();
        }

        // --- NEW SECURE HANDLER FOR TICKETS ---
        public async Task<IActionResult> OnPostGetStreamTicketAsync()
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                var url = $"{_apiOptions.Value.BaseUrl}/api/admin/fetch/stream-ticket";

                var request = new HttpRequestMessage(HttpMethod.Post, url);

                // SECURE: Attach the API key here on the server. The browser never sees it.
                request.Headers.Add("X-Api-Key", _gatewayConfig.AdminKey);

                var response = await client.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Failed to acquire stream ticket from API. Status: {StatusCode}", response.StatusCode);
                    return new StatusCodeResult((int)response.StatusCode);
                }

                var json = await response.Content.ReadAsStringAsync();
                return Content(json, "application/json");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while requesting stream ticket proxy.");
                return new StatusCodeResult(500);
            }
        }
        // --------------------------------------

        public async Task<IActionResult> OnPostStartDownloadAsync([FromQuery] string repoId)
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                var url = $"{_apiOptions.Value.BaseUrl}/api/admin/fetch/start?repoId={Uri.EscapeDataString(repoId)}";

                _logger.LogInformation("Requesting background asset acquisition for: {RepoId}", repoId);
                var response = await client.PostAsync(url, null);

                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    ModelState.AddModelError(string.Empty, $"Failed to start file acquisition channel: {error}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Critical fault attempting interaction with background fetch endpoint.");
            }

            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostCancelDownloadAsync([FromQuery] string repoId)
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                var url = $"{_apiOptions.Value.BaseUrl}/api/admin/fetch/cancel?repoId={Uri.EscapeDataString(repoId)}";

                _logger.LogInformation("Sending structural kill handle packet for: {RepoId}", repoId);
                await client.PostAsync(url, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing download termination commands.");
            }

            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostLoadAsync()
        {
            _logger.LogInformation("Processing model deployment request. Target path: {RepoId}", ModelSettings.RepoId);

            if (!ModelState.IsValid)
            {
                _logger.LogWarning("Invalid model configuration state submitted.");
                await LoadModelsDataAsync();
                return Page();
            }

            try
            {
                var client = _httpClientFactory.CreateClient();
                var url = $"{_apiOptions.Value.BaseUrl}/api/admin/models/load";

                ModelSettings requestBody = new()
                {
                    RepoId = ModelSettings.RepoId,
                    ContextSize = ModelSettings.ContextSize,
                    GpuLayerCount = ModelSettings.GpuLayerCount,
                    FlashAttention = ModelSettings.FlashAttention,
                    Threads = ModelSettings.Threads > 0 ? ModelSettings.Threads : 4,
                    MaxContexts = ModelSettings.MaxContexts > 0 ? ModelSettings.MaxContexts : 2,
                    BatchSize = ModelSettings.BatchSize,
                    Embeddings = ModelSettings.Embeddings,
                    KvCacheQuantization = ModelSettings.KvCacheQuantization,
                    MainGPU = ModelSettings.MainGPU,
                    UseMemoryLock = ModelSettings.UseMemoryLock
                };

                _logger.LogInformation("Sending structural load request for model: {RepoId}", ModelSettings.RepoId);
                var response = await client.PostAsJsonAsync(url, requestBody);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Model {RepoId} successfully allocated inside runtime bounds.", ModelSettings.RepoId);
                    return RedirectToPage();
                }

                var errorText = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Gateway core rejected model initialization. Core response: {Response}", errorText);
                ModelState.AddModelError(string.Empty, $"Gateway rejected initialization: {errorText}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Critical failure during model activation endpoint call.");
                ModelState.AddModelError(string.Empty, $"Internal client error: {ex.Message}");
            }

            await LoadModelsDataAsync();
            return Page();
        }

        public async Task<IActionResult> OnPostUnloadAsync([FromQuery] string repoId)
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                var url = $"{_apiOptions.Value.BaseUrl}/api/admin/models/unload";

                _logger.LogInformation("Requesting VRAM/RAM release for model: {RepoId}", repoId);

                var response = await client.PostAsJsonAsync(url, new { RepoId = repoId });

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("VRAM successfully cleared for model: {RepoId}", repoId);
                    return RedirectToPage();
                }

                _logger.LogError("Backend refused to unload the model. Status code: {Code}", response.StatusCode);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send unload command to gateway core.");
            }

            return RedirectToPage();
        }

        private async Task LoadModelsDataAsync()
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());

                var response = await client.GetAsync($"{_apiOptions.Value.BaseUrl}/api/admin/models");
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    Models = JsonSerializer.Deserialize<List<ModelViewItem>>(json, options) ?? new();
                }

                var activeResponse = await client.GetAsync($"{_apiOptions.Value.BaseUrl}/api/admin/models/active/telemetry");

                ActiveModelRepoIds.Clear();
                ActiveModelsTelemetry.Clear();

                if (activeResponse.IsSuccessStatusCode)
                {
                    var telemetryData = await activeResponse.Content.ReadFromJsonAsync<List<ModelRegistryStatus>>(options);

                    if (telemetryData != null)
                    {
                        foreach (var status in telemetryData)
                        {
                            if (!string.IsNullOrWhiteSpace(status.RepoId))
                            {
                                ActiveModelRepoIds.Add(status.RepoId);
                                ActiveModelsTelemetry[status.RepoId] = status;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching telemetry and models array from administrative plane API.");
            }
        }
    }
}