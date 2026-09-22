// File: src/InstantAIGate.Native/Inference/LlamaModelLocator.cs
namespace InstantAIGate.Native.Inference;

using InstantAIGate.Core.Dtos.Config;
using InstantAIGate.Core.Interfaces.Inference;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

public class LlamaModelLocator : IModelLocator
{
    private readonly ILogger<LlamaModelLocator> _logger;
    private readonly StorageSettings _storageSettings;

    public LlamaModelLocator(ILogger<LlamaModelLocator> logger, IOptions<StorageSettings> storageSettings)
    {
        _logger = logger;
        _storageSettings = storageSettings.Value;
    }

    public Task<ResolvedModelPaths> ResolvePathsAsync(string repoId, bool visionSupport, CancellationToken ct = default)
    {
        string modelPath = Path.Combine(_storageSettings.ModelsDirectory, repoId);
        string? projectorPath = null;

        if (!File.Exists(modelPath) && !Directory.Exists(modelPath))
            throw new FileNotFoundException($"Model path not found: {modelPath}");

        if (Directory.Exists(modelPath))
        {
            var primaryModel = Directory.GetFiles(modelPath, "*.gguf")
                .FirstOrDefault(f => !f.Contains("mmproj", StringComparison.OrdinalIgnoreCase) && !f.Contains("clip", StringComparison.OrdinalIgnoreCase))
                ?? throw new FileNotFoundException($"No primary .gguf model found in directory: {modelPath}");

            if (visionSupport)
            {
                projectorPath = Directory.GetFiles(modelPath, "*.gguf")
                    .FirstOrDefault(f => f.Contains("mmproj", StringComparison.OrdinalIgnoreCase) || f.Contains("clip", StringComparison.OrdinalIgnoreCase));
            }
            modelPath = primaryModel;
        }
        else if (visionSupport)
        {
            var currentFileName = Path.GetFileName(modelPath);
            var directory = Path.GetDirectoryName(modelPath);
            bool isProjector = currentFileName != null 
                && (currentFileName.Contains("mmproj", StringComparison.OrdinalIgnoreCase) 
                || currentFileName.Contains("clip", StringComparison.OrdinalIgnoreCase));

            if (isProjector && !string.IsNullOrEmpty(directory))
            {
                var textModel = Directory.GetFiles(directory, "*.gguf")
                    .FirstOrDefault(f => !f.Contains("mmproj", StringComparison.OrdinalIgnoreCase) && !f.Contains("clip", StringComparison.OrdinalIgnoreCase));

                if (!string.IsNullOrEmpty(textModel))
                {
                    _logger.LogWarning("Auto-corrected modelPath. Switched from projector '{Proj}' to text model '{Text}'", currentFileName, Path.GetFileName(textModel));
                    projectorPath = modelPath;
                    modelPath = textModel;
                }
            }
            else if (string.IsNullOrEmpty(projectorPath) && !string.IsNullOrEmpty(directory))
            {
                projectorPath = Directory.GetFiles(directory, "*.gguf")
                    .FirstOrDefault(f => f.Contains("mmproj", StringComparison.OrdinalIgnoreCase) || f.Contains("clip", StringComparison.OrdinalIgnoreCase));
            }
        }

        if (visionSupport && string.IsNullOrEmpty(projectorPath))
        {
            throw new FileNotFoundException("Vision support is requested, but no projector (mmproj/clip) file was found.");
        }

        long sizeBytes = new FileInfo(modelPath).Length;
        if (!string.IsNullOrEmpty(projectorPath))
        {
            sizeBytes += new FileInfo(projectorPath).Length;
        }

        return Task.FromResult(new ResolvedModelPaths(modelPath, projectorPath, sizeBytes));
    }
}

