using InstantAIGate.Admin.Config;
using InstantAIGate.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace InstantAIGate.Admin.Pages
{
    public class EmbeddingLabModel : PageModel
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IOptions<APIClientOptions> _apiOptions;
        private readonly ILogger<EmbeddingLabModel> _logger;

        public string APIUrl { get; }
        public string? ActiveModel { get; set; }

        public EmbeddingLabModel(
            IHttpClientFactory httpClientFactory,
            IOptions<APIClientOptions> apiOptions,
            ILogger<EmbeddingLabModel> logger)
        {
            _httpClientFactory = httpClientFactory;
            _apiOptions = apiOptions;
            _logger = logger;
            APIUrl = _apiOptions.Value.PublicUrl;
        }

        public async Task<IActionResult> OnGetAsync()
        {
            await LoadActiveModelAsync();
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
                        // In a single-model architecture, we simply take the first loaded model
                        if (element.TryGetProperty("repoId", out var repoProp))
                        {
                            var id = repoProp.GetString();
                            if (!string.IsNullOrEmpty(id))
                            {
                                ActiveModel = id;
                                break;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve the active execution core for the embedding lab.");
            }
        }
    }
}