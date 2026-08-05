using System;
using System.Collections.Generic;
using System.Text;

namespace InstantAIGate.Application.ModelManagement.Conteracts
{
    public interface IModelConfigurationService
    {
        IEnumerable<string> GetActiveModelRepoIds();
        string GetProviderConfig(string providerName, string key);
    }
}
