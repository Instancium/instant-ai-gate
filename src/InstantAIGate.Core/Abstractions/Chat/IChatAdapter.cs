using InstantAIGate.Core.DTOs.Chat;
using InstantAIGate.Core.DTOs.Common;
using System;
using System.Collections.Generic;
using System.Text;

namespace InstantAIGate.Core.Abstractions.Chat
{
    /// <summary>
    /// Engine-agnostic adapter for chat-based AI inference (ONNX, GGUF, API).
    /// </summary>
    public interface IChatAdapter : IDisposable
    {
        /// <summary>
        /// Indicates if the loaded model supports vision or audio inputs.
        /// </summary>
        bool SupportsMultimodal { get; }

        /// <summary>
        /// Initializes the adapter and loads the model into memory asynchronously.
        /// </summary>
        /// <param name="manifest">The abstract model configuration.</param>
        /// <param name="cancellationToken">A token to cancel the loading process.</param>
        Task InitializeAsync(
            ModelManifest manifest,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Generates a streaming text response based on the unified chat request.
        /// </summary>
        /// <param name="request">The chat request containing messages and generation parameters.</param>
        /// <param name="cancellationToken">A token to abort the generation process.</param>
        IAsyncEnumerable<string> GenerateStreamAsync(
            ChatRequest request,
            CancellationToken cancellationToken = default);
    }
}
