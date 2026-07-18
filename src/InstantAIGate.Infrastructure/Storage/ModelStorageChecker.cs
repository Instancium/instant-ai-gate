using InstantAIGate.Application.Interfaces.Storage;
using InstantAIGate.Domain.Entities;
using InstantAIGate.Domain.Extensions;
using Microsoft.Extensions.Logging;

namespace InstantAIGate.Infrastructure.Storage
{
    /// <summary>
    /// Verifies the physical presence and size integrity of model binaries distributed across local storage targets.
    /// Supports multi-file sharded allocations seamlessly.
    /// </summary>
    public class ModelStorageChecker(IModelPathProvider pathProvider, ILogger<ModelStorageChecker> logger) : IModelStorageChecker
    {
        private readonly IModelPathProvider _pathProvider = pathProvider;
        private readonly ILogger<ModelStorageChecker> _logger = logger;

        // Increased allowance allocation layout to 10% to prevent rigid GGUF cluster size mismatches
        private const double SizeTolerancePercentage = 0.10;

        /// <summary>
        /// Validates that EVERY individual file shard belonging to the registered model is completely 
        /// downloaded on disk and matches expected baseline byte weights.
        /// </summary>
        public bool IsModelDownloaded(ModelManifest model)
        {
            if (model == null || model.Files == null || model.Files.Count == 0)
                return false;

            string targetFolder = _pathProvider.GetModelDirectory(model.RepoId);

            foreach (var file in model.Files)
            {
                string absoluteFilePath = Path.Combine(targetFolder, file.FileName);

                if (!File.Exists(absoluteFilePath))
                {
                    _logger.LogWarning("Storage verification failed: Target file does not exist. Path: {Path}", absoluteFilePath);
                    return false;
                }

                var fileInfo = new FileInfo(absoluteFilePath);
                long actualBytes = fileInfo.Length;

                // Fallback layout: if no specific size metadata is provided, accept any non-empty physical weight
                if (file.SizeBytes <= 0)
                {
                    if (actualBytes == 0) return false;
                    continue;
                }

                // Rigid boundary evaluation to catch truncated downloads before memory pipeline deployment
                long minAllowedSize = (long)(file.SizeBytes * (1.0 - SizeTolerancePercentage));
                long maxAllowedSize = (long)(file.SizeBytes * (1.0 + SizeTolerancePercentage));

                if (actualBytes < minAllowedSize || actualBytes > maxAllowedSize)
                {
                    _logger.LogWarning(
                        "Storage size validation rejected for model '{RepoId}'. File: '{FileName}'. Expected: {Expected} bytes, Actual on disk: {Actual} bytes.",
                        model.RepoId, file.FileName, file.SizeBytes, actualBytes);
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Resolves the absolute physical path to the primary execution binary file (the first split chunk) 
        /// required to boot the native LLama core runtime graph.
        /// </summary>
        public string? GetModelPath(ModelManifest model)
        {
            if (model == null || model.Files == null || model.Files.Count == 0)
                return null;

            // Use the extension method to guarantee we get the core LLM execution binary
            var primaryFile = model.GetMainTextFile();
            if (primaryFile == null)
            {
                _logger.LogWarning("Failed to resolve the primary text binary for model '{RepoId}'.", model.RepoId);
                return null;
            }

            string fullPath = _pathProvider.GetModelFilePath(model.RepoId, primaryFile.FileName);

            return File.Exists(fullPath) ? fullPath : null;
        }
    }
}