namespace InstantAIGate.Core.Services.Inference;

using InstantAIGate.Core.Dtos.Config;
using InstantAIGate.Core.Interfaces.Inference;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;

/// <summary>
/// Default <see cref="IEmbeddingModelResolver"/> implementation backed by <see cref="IModelManager"/>.
/// Resolution order:
/// 1. Requested model, when it is active AND embedding-capable.
/// 2. Dedicated embedding model loaded in the pool (auto-routing),
///    when the requested model is not embedding-capable or not specified.
/// 3. Failure with a descriptive error otherwise.
/// </summary>
public sealed class EmbeddingModelResolver : IEmbeddingModelResolver
{
    private readonly IModelManager _modelManager;
    private readonly ILogger<EmbeddingModelResolver> _logger;

    public EmbeddingModelResolver(IModelManager modelManager, ILogger<EmbeddingModelResolver> logger)
    {
        _modelManager = modelManager ?? throw new ArgumentNullException(nameof(modelManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string Resolve(string? requestedRepoId)
    {
        if (!string.IsNullOrWhiteSpace(requestedRepoId))
        {
            ModelSettings? explicitSettings = _modelManager.GetModelSettings(requestedRepoId);
            if (explicitSettings is { Embeddings: true })
            {
                return explicitSettings.RepoId;
            }

            ModelSettings? dedicatedEmbedding = FindDedicatedEmbeddingModel();
            if (dedicatedEmbedding != null)
            {
                if (explicitSettings != null)
                {
                    _logger.LogInformation(
                        "Requested model '{Requested}' is not embedding-capable. Auto-routing embedding request to '{Target}'.",
                        requestedRepoId, dedicatedEmbedding.RepoId);
                }

                return dedicatedEmbedding.RepoId;
            }

            if (explicitSettings != null)
            {
                throw new InvalidOperationException(
                    $"Model '{requestedRepoId}' is not configured for embeddings and no dedicated embedding model is loaded. " +
                    "Load an embedding-capable model first (e.g., /load qwen3-embedding-0.6b Embedding).");
            }

            throw new InvalidOperationException($"Model '{requestedRepoId}' is not active.");
        }

        return FindDedicatedEmbeddingModel()?.RepoId
            ?? throw new InvalidOperationException(
                "No embedding-capable model is loaded on the gateway. Load an embedding model first.");
    }

    private ModelSettings? FindDedicatedEmbeddingModel() =>
        _modelManager.ActiveModels?.Values
            .FirstOrDefault(m => m.Embeddings || m.Type == ModelType.Embedding);
}
