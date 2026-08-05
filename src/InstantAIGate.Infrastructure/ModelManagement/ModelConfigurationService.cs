using System;
using System.Collections.Generic;
using InstantAIGate.Application.Interfaces;
using InstantAIGate.Application.ModelManagement.Conteracts;
using Microsoft.Extensions.Configuration;

namespace InstantAIGate.Infrastructure.ModelManagement
{
    public class ModelConfigurationService : IModelConfigurationService
    {
        private readonly IConfiguration _configuration;

        public ModelConfigurationService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public IEnumerable<string> GetActiveModelRepoIds()
        {
            var section = _configuration.GetSection("ActiveModels");
            return section.Get<string[]>() ?? Array.Empty<string>();
        }

        public string GetProviderConfig(string providerName, string key)
        {
            return _configuration[$"Providers:{providerName}:{key}"] ?? string.Empty;
        }
    }
}