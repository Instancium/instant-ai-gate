using InstantAIGate.Application.Interfaces;
using InstantAIGate.Application.ModelManagement.Conteracts;
using InstantAIGate.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;

namespace InstantAIGate.Infrastructure.ModelManagement
{
    public class S3Resolver : IModelManifestResolver
    {
        private readonly HttpClient _httpClient;
        private readonly IModelConfigurationService _configService;

        public S3Resolver(HttpClient httpClient, IModelConfigurationService configService)
        {
            _httpClient = httpClient;
            _configService = configService;
        }

        public async Task<IEnumerable<ModelFile>> ResolveManifestAsync(SupportedModelDefinition definition, CancellationToken cancellationToken)
        {
            var endpoint = _configService.GetProviderConfig("S3Private", "Endpoint");
            var bucket = _configService.GetProviderConfig("S3Private", "Bucket");
            var authHeader = _configService.GetProviderConfig("S3Private", "ApiToken");

            var url = $"{endpoint}/{bucket}?list-type=2&prefix={definition.TargetDirectoryPath}";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);

            if (definition.Tier == Domain.Enums.ModelTier.APIUsing && !string.IsNullOrEmpty(authHeader))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authHeader);
            }

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var xmlContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var doc = XDocument.Parse(xmlContent);
            var files = new List<ModelFile>();
            var ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;

            var contents = doc.Descendants(ns + "Contents");
            foreach (var content in contents)
            {
                var key = content.Element(ns + "Key")?.Value;
                var sizeStr = content.Element(ns + "Size")?.Value;

                if (key != null && sizeStr != null && long.TryParse(sizeStr, out long size))
                {
                    if (key.EndsWith("/"))
                    {
                        continue;
                    }

                    files.Add(new ModelFile
                    {
                        RelativePath = key,
                        Url = $"{endpoint}/{bucket}/{key}",
                        SizeBytes = size
                    });
                }
            }

            return files;
        }
    }
}
