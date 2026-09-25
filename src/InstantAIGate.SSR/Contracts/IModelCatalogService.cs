using InstantAIGate.SSR.Dtos;

namespace InstantAIGate.SSR.Contracts
{
    public interface IModelCatalogService
    {
        Task<IReadOnlyList<CatalogModelEntry>> GetSupportedModelsAsync(CancellationToken ct = default);
        Task<CatalogModelEntry?> FindModelByIdAsync(string modelId, CancellationToken ct = default);
    }
}
