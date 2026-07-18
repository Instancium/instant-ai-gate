using InstantAIGate.Admin.Config;
using InstantAIGate.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace InstantAIGate.Admin.Pages
{
    public class LLMConsoleModel : PageModel
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IOptions<APIClientOptions> _apiOptions;
        private readonly ILogger<LLMConsoleModel> _logger;

        public string APIUrl { get; set; }
        public string? SelectedRepoId { get; set; }
        public List<string> ActiveModels { get; set; } = new();
        public string? WarningMessage { get; set; }

        public LLMConsoleModel(
            IHttpClientFactory httpClientFactory,
            IOptions<APIClientOptions> apiOptions,
            ILogger<LLMConsoleModel> logger)
        {
            _httpClientFactory = httpClientFactory;
            _apiOptions = apiOptions;
            _logger = logger;
            APIUrl = _apiOptions.Value.PublicUrl;
        }

        public async Task<IActionResult> OnGetAsync(string? repoId)
        {
            SelectedRepoId = repoId;
            await LoadActiveModelsAsync();

            if (!string.IsNullOrEmpty(SelectedRepoId) && !ActiveModels.Contains(SelectedRepoId))
            {
                WarningMessage = $"Warning: Model '{repoId}' is not currently initialized in VRAM.";
            }

            return Page();
        }

        private async Task LoadActiveModelsAsync()
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                var response = await client.GetAsync($"{_apiOptions.Value.BaseUrl}/api/admin/models/active/telemetry");

                if (response.IsSuccessStatusCode)
                {
                    using var jsonDoc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
                    var elements = jsonDoc.RootElement.EnumerateArray();

                    foreach (var element in elements)
                    {
                        if (element.TryGetProperty("type", out var typeProp)
                            && typeProp.TryGetInt32(out int typeValue))
                        {
                            if ((ModelType)typeValue == ModelType.Llm)
                            {
                                if (element.TryGetProperty("repoId", out var repoProp))
                                {
                                    var id = repoProp.GetString();
                                    if (!string.IsNullOrEmpty(id))
                                    {
                                        ActiveModels.Add(id);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve the list of active execution cores for the console.");
            }
        }
    }
}