using InstantAIGate.Application.Interfaces;
using InstantAIGate.Application.ModelManagement.Conteracts;
using InstantAIGate.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;

namespace InstantAIGate.Infrastructure.ModelManagement
{
    public class HuggingFaceResolver : IModelManifestResolver
    {
        private readonly HttpClient _httpClient;
        private readonly IModelConfigurationService _configService;

        public HuggingFaceResolver(HttpClient httpClient, IModelConfigurationService configService)
        {
            _httpClient = httpClient;
            _configService = configService;
        }

        public async Task<IEnumerable<ModelFile>> ResolveManifestAsync(SupportedModelDefinition definition, CancellationToken cancellationToken)
        {
            var files = new List<ModelFile>();
            await TraverseDirectoryAsync(definition, definition.TargetDirectoryPath, files, cancellationToken);
            return files;
        }

        private async Task TraverseDirectoryAsync(SupportedModelDefinition definition, string currentPath, List<ModelFile> files, CancellationToken cancellationToken)
        {
            var token = _configService.GetProviderConfig("HuggingFace", "ApiToken");
            var requestUrl = $"https://huggingface.co/api/models/{definition.RepoId}/tree/main/{currentPath}";
            using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);

            if (definition.Tier == Domain.Enums.ModelTier.APIUsing && !string.IsNullOrEmpty(token))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var items = await response.Content.ReadFromJsonAsync<List<HuggingFaceItem>>(cancellationToken: cancellationToken);
            if (items == null)
            {
                return;
            }

            foreach (var item in items)
            {
                if (item.Type == "directory")
                {
                    await TraverseDirectoryAsync(definition, item.Path, files, cancellationToken);
                }
                else if (item.Type == "file")
                {
                    files.Add(new ModelFile
                    {
                        RelativePath = item.Path,
                        Url = $"https://huggingface.co/{definition.RepoId}/resolve/main/{item.Path}",
                        SizeBytes = item.Size
                    });
                }
            }
        }
    }
}
