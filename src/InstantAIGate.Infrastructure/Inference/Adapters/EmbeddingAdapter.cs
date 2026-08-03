using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using InstantAIGate.Application.Interfaces.Inference;
using InstantAIGate.Infrastructure.Inference.Facades;
using InstantAIGate.Infrastructure.Inference.Native;
using Microsoft.Extensions.Logging;

namespace InstantAIGate.Infrastructure.Inference.Adapters
{
    public class EmbeddingAdapter : IEmbeddingAdapter
    {
        private readonly IEmbeddingEngineFacade _embeddingFacade;
        private readonly ModelManager _modelManager;
        private readonly ILogger<EmbeddingAdapter> _logger;

        public EmbeddingAdapter(
            IEmbeddingEngineFacade embeddingFacade,
            ModelManager modelManager,
            ILogger<EmbeddingAdapter> logger)
        {
            _embeddingFacade = embeddingFacade ?? throw new ArgumentNullException(nameof(embeddingFacade));
            _modelManager = modelManager ?? throw new ArgumentNullException(nameof(modelManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<IReadOnlyList<float[]>> GetEmbeddingAsync(string model, List<string> inputs, CancellationToken ct = default)
        {
            if (inputs == null || inputs.Count == 0)
            {
                return Array.Empty<float[]>();
            }

            var settings = _modelManager.GetActiveSettings();
            if (settings == null)
            {
                throw new InvalidOperationException("No active model is currently loaded in the system.");
            }

            string activeRepoId = settings.RepoId;

            if (!string.Equals(model, activeRepoId, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("Embedding client requested '{RequestedModel}', dynamically routing to active model '{ActiveModel}'.", model, activeRepoId);
            }

            var inferenceSettings = new InferenceSettings
            {
                ModelId = activeRepoId,
                BatchSize = (int)(settings.BatchSize > 0 ? settings.BatchSize : 512),
                MaxTokens = (int)(settings.ContextSize > 0 ? settings.ContextSize : 2048)
            };

            return await _embeddingFacade.GetEmbeddingsAsync(inputs, inferenceSettings, ct);
        }
    }
}