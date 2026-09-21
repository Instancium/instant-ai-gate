namespace InstantAIGate.Core.Interfaces.Inference;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public interface IModelValidator
{
    /// <summary>
    /// Validates the integrity of the downloaded files using backend-specific logic 
    /// (e.g., LlamaNative VocabOnly for GGUF, or ONNX session initialization check).
    /// </summary>
    Task<bool> ValidateIntegrityAsync(IEnumerable<string> filePaths, CancellationToken ct = default);
}