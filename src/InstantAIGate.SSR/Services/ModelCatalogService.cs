namespace InstantAIGate.SSR.Services;

using InstantAIGate.SSR.Contracts;
using InstantAIGate.SSR.Dtos;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

public class ModelCatalogService : IModelCatalogService
{
    private readonly ILogger<ModelCatalogService> _logger;
    private readonly string _catalogFilePath;
    private IReadOnlyList<CatalogModelEntry>? _cachedCatalog;

    public ModelCatalogService(ILogger<ModelCatalogService> logger)
    {
        _logger = logger;
        // Locate config/model_catalog.json relative to the execution context
        _catalogFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", "model_catalog.json");
    }

    public async Task<IReadOnlyList<CatalogModelEntry>> GetSupportedModelsAsync(CancellationToken ct = default)
    {
        if (_cachedCatalog != null)
        {
            return _cachedCatalog;
        }

        if (!File.Exists(_catalogFilePath))
        {
            _logger.LogWarning("Catalog file not found at {Path}. Returning empty catalog.", _catalogFilePath);
            return new List<CatalogModelEntry>();
        }

        try
        {
            using var stream = File.OpenRead(_catalogFilePath);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            var entries = await JsonSerializer.DeserializeAsync<List<CatalogModelEntry>>(stream, options, ct);
            _cachedCatalog = entries?.AsReadOnly() ?? new List<CatalogModelEntry>().AsReadOnly();

            return _cachedCatalog;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse model_catalog.json. Format is invalid.");
            throw;
        }
    }

    public async Task<CatalogModelEntry?> FindModelByIdAsync(string modelId, CancellationToken ct = default)
    {
        var models = await GetSupportedModelsAsync(ct);
        return models.FirstOrDefault(m => m.Id.Equals(modelId, StringComparison.OrdinalIgnoreCase));
    }
}