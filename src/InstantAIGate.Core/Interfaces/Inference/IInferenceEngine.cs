namespace InstantAIGate.Core.Interfaces.Inference;

using InstantAIGate.Core.Dtos.Config;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Provides high-level inference capabilities such as tokenization and text generation.
/// </summary>
public interface IInferenceEngine
{
    /// <summary>
    /// Tokenizes the input text into an array of token IDs.
    /// </summary>
    /// <param name="repoId">The repository identifier of the loaded model.</param>
    /// <param name="text">The text to tokenize.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An array of integer token IDs.</returns>
    Task<int[]> TokenizeDataAsync(string modelId, string text, CancellationToken ct = default);

    /// <summary>
    /// Generates a text response based on the provided prompt tokens.
    /// </summary>
    /// <param name="repoId">The repository identifier of the loaded model.</param>
    /// <param name="promptTokens">The tokenized prompt.</param>
    /// <param name="settings">Inference configuration parameters.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The generated text string.</returns>
    IAsyncEnumerable<string> StreamGenerationAsync(string modelId, string prompt, InferenceSettings settings, CancellationToken ct = default);



}