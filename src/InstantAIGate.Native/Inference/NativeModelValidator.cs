namespace InstantAIGate.Native.Inference;

using InstantAIGate.Core.Interfaces.Inference;
using InstantAIGate.Native.Bindings;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

public class NativeModelValidator : IModelValidator
{
    private readonly ILogger<NativeModelValidator> _logger;

    public NativeModelValidator(ILogger<NativeModelValidator> logger)
    {
        _logger = logger;
    }

    public Task<bool> ValidateIntegrityAsync(IEnumerable<string> filePaths, CancellationToken ct = default)
    {
        var paths = filePaths.ToList();
        if (!paths.Any()) return Task.FromResult(false);

        // Ensure native library is loaded before calling
        if (!NativeLibraryLoader.IsLoaded)
        {
            NativeLibraryLoader.Load();
        }

        var modelParams = LlamaNative.llama_model_default_params();
        modelParams.VocabOnly = true; // Read headers/metadata without tensor allocation
        modelParams.UseExtraBufts = false;
        modelParams.NoAlloc = true;

        IntPtr modelHandle = IntPtr.Zero;
        try
        {
            // Pragmatic: If only one file, use load_from_file. 
            // If multiple, assume GGUF splits and use load_from_splits (requires implementation mapping).
            // For now, we validate the primary (or first) file header to ensure basic GGUF integrity.
            string primaryFile = paths.First();

            modelHandle = LlamaNative.llama_model_load_from_file(primaryFile, in modelParams, (nuint)primaryFile.Length);

            bool isValid = modelHandle != IntPtr.Zero;

            if (isValid)
            {
                _logger.LogInformation("GGUF Integrity check passed for {File}", primaryFile);
            }
            else
            {
                _logger.LogWarning("GGUF Integrity check failed. Native engine rejected the file {File}", primaryFile);
            }

            return Task.FromResult(isValid);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception during native model validation.");
            return Task.FromResult(false);
        }
        finally
        {
            if (modelHandle != IntPtr.Zero)
            {
                LlamaNative.llama_model_free(modelHandle);
            }
        }
    }
}