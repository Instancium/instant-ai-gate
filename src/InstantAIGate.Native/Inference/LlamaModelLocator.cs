// File: src/InstantAIGate.Native/Inference/LlamaModelLocator.cs
namespace InstantAIGate.Native.Inference;

using InstantAIGate.Core.Dtos.Config;
using InstantAIGate.Core.Interfaces.Inference;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

public class LlamaModelLocator : IModelLocator
{
    private readonly ILogger<LlamaModelLocator> _logger;

    public LlamaModelLocator(ILogger<LlamaModelLocator> logger)
    {
        _logger = logger;
    }

    public Task<ResolvedModelPaths> ResolvePathsAsync(ModelSettings config, CancellationToken ct = default)
    {
        string modelPath = config.ModelPath;
        string? projectorPath = config.ProjectorPath;

        if (!File.Exists(modelPath) && !Directory.Exists(modelPath))
            throw new FileNotFoundException($"Model path not found: {modelPath}");

        // Handle directory-based loading (common for CLI shortcuts)
        if (Directory.Exists(modelPath))
        {
            modelPath = Directory.GetFiles(modelPath, "*.gguf")
                .FirstOrDefault(f => !f.Contains("mmproj", StringComparison.OrdinalIgnoreCase) && !f.Contains("clip", StringComparison.OrdinalIgnoreCase))
                ?? throw new FileNotFoundException($"No primary .gguf model found in directory: {modelPath}");

            if (config.VisionSupport && string.IsNullOrEmpty(projectorPath))
            {
                var dir = Path.GetDirectoryName(modelPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    projectorPath = Directory.GetFiles(dir, "*.gguf")
                        .FirstOrDefault(f => f.Contains("mmproj", StringComparison.OrdinalIgnoreCase) || f.Contains("clip", StringComparison.OrdinalIgnoreCase));
                }
            }
        }
        else if (config.VisionSupport)
        {
            // The original logic you had inside ModelProvider
            var currentFileName = Path.GetFileName(modelPath);
            var directory = Path.GetDirectoryName(modelPath);
            bool isProjector = currentFileName != null && (currentFileName.Contains("mmproj", StringComparison.OrdinalIgnoreCase) || currentFileName.Contains("clip", StringComparison.OrdinalIgnoreCase));

            if (isProjector && !string.IsNullOrEmpty(directory))
            {
                var textModel = Directory.GetFiles(directory, "*.gguf")
                    .FirstOrDefault(f => !f.Contains("mmproj", StringComparison.OrdinalIgnoreCase) && !f.Contains("clip", StringComparison.OrdinalIgnoreCase));

                if (!string.IsNullOrEmpty(textModel))
                {
                    _logger.LogWarning("Auto-corrected config.ModelPath. Switched from projector '{Proj}' to text model '{Text}'", currentFileName, Path.GetFileName(textModel));
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

        if (config.VisionSupport && string.IsNullOrEmpty(projectorPath))
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