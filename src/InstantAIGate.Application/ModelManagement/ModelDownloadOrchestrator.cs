using InstantAIGate.Application.Interfaces;
using InstantAIGate.Application.ModelManagement.Conteracts;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace InstantAIGate.Application.ModelManagement
{
    public class ModelDownloadOrchestrator : IModelDownloadOrchestrator
    {
        private readonly IModelConfigurationService _configService;
        private readonly IModelCatalog _catalog;
        private readonly IModelSynchronizer _synchronizer;

        public ModelDownloadOrchestrator(
            IModelConfigurationService configService,
            IModelCatalog catalog,
            IModelSynchronizer synchronizer)
        {
            _configService = configService;
            _catalog = catalog;
            _synchronizer = synchronizer;
        }

        public async IAsyncEnumerable<AggregateDownloadProgress> ExecuteDownloadAsync(
            string repoId,
            string variantId,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var activeModels = _configService.GetActiveModelRepoIds();
            if (!activeModels.Contains(repoId))
            {
                throw new UnauthorizedAccessException("Requested model is not enabled in the application configuration.");
            }

            var modelDefinition = _catalog.GetModel(repoId);
            if (modelDefinition == null)
            {
                throw new KeyNotFoundException("Requested model is not present in the authorized whitelist catalog.");
            }

            var variant = modelDefinition.Variants.FirstOrDefault(v => v.VariantId.Equals(variantId, StringComparison.OrdinalIgnoreCase));
            if (variant == null)
            {
                throw new KeyNotFoundException($"Variant '{variantId}' is not defined for model '{repoId}'.");
            }

            var progressStream = _synchronizer.SynchronizeModelAsync(modelDefinition, variant, cancellationToken);

            await foreach (var progress in progressStream.WithCancellation(cancellationToken))
            {
                yield return progress;
            }
        }
    }
}
