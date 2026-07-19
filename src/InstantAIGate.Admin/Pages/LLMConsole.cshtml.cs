using InstantAIGate.Admin.Config;
using InstantAIGate.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace InstantAIGate.Admin.Pages
{
    public class LLMConsoleModel : PageModel
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IOptions<APIClientOptions> _apiOptions;
        private readonly ILogger<LLMConsoleModel> _logger;

        public string APIUrl { get; set; }
        public string? ActiveModel { get; set; }
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

        public async Task<IActionResult> OnGetAsync()
        {
            await LoadActiveModelAsync();

            if (string.IsNullOrEmpty(ActiveModel))
            {
                WarningMessage = "Warning: No active model is currently initialized in VRAM.";
            }

            return Page();
        }

        private async Task LoadActiveModelAsync()
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
                                        ActiveModel = id;
                                        break; // Single-model paradigm: we only need the first active model
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve the active execution core for the console.");
            }
        }
    }
}