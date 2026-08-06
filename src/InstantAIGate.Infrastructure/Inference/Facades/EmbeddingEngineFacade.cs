using InstantAIGate.Infrastructure.Inference.Facades;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace InstantAIGate.Infrastructure.Inference.Facades
{


    /// <summary>
    /// Temporary stub implementation of IEmbeddingEngineFacade to unblock
    /// solution compilation during the ONNX Runtime migration.
    /// </summary>
    public class EmbeddingEngineFacade : IEmbeddingEngineFacade
    {
        private readonly ModelManager _modelManager;
        private readonly ILogger<EmbeddingEngineFacade> _logger;

        public EmbeddingEngineFacade(ModelManager modelManager, ILogger<EmbeddingEngineFacade> logger)
        {
            _modelManager = modelManager ?? throw new ArgumentNullException(nameof(modelManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public Task<IReadOnlyList<float[]>> GetEmbeddingsAsync(List<string> inputs, InferenceSettings settings, CancellationToken ct = default)
        {
            _logger.LogWarning("GetEmbeddingsAsync stub called for model {ModelId}.", settings?.ModelId);

            IReadOnlyList<float[]> emptyResults = Array.Empty<float[]>();
            return Task.FromResult(emptyResults);
        }
    }
}