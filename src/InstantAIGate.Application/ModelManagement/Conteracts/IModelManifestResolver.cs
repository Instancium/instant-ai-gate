using InstantAIGate.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace InstantAIGate.Application.ModelManagement.Conteracts
{
    public interface IModelManifestResolver
    {
        Task<IEnumerable<ModelFile>> ResolveManifestAsync(SupportedModelDefinition definition, ModelVariant variant, CancellationToken cancellationToken);
    }
}
