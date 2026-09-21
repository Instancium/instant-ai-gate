using InstantAIGate.SSR.Dtos;
using System;
using System.Collections.Generic;
using System.Text;

namespace InstantAIGate.SSR.Contracts
{
    public interface IModelCatalogService
    {
        Task<IReadOnlyList<CatalogModelEntry>> GetSupportedModelsAsync(CancellationToken ct = default);
        Task<CatalogModelEntry?> FindModelByIdAsync(string modelId, CancellationToken ct = default);
    }
}
