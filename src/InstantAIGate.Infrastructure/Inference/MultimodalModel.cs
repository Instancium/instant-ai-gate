using InstantAIGate.Infrastructure.Inference.Native;
using InstantAIGate.Infrastructure.Inference.Vision;

namespace InstantAIGate.Infrastructure.Inference
{
    /// <summary>
    /// Represents a unified multimodal model consisting of text weights and an optional vision projector.
    /// Manages the lifecycle of both components to ensure synchronized resource release.
    /// </summary>
    public sealed class MultimodalModel : IDisposable
    {
        public LlamaModel TextModel { get; }
        public VisionContext? VisionContext { get; }

        /// <summary>
        /// Semaphore to synchronize access to the shared Vision Encoder.
        /// </summary>
        public SemaphoreSlim? VisionLock { get; }

        public bool IsMultimodal => VisionContext != null;

        public MultimodalModel(
            LlamaModel textModel,
            VisionContext? visionContext = null,
            SemaphoreSlim? visionLock = null)
        {
            TextModel = textModel ?? throw new ArgumentNullException(nameof(textModel));
            VisionContext = visionContext;
            VisionLock = visionLock;
        }

        public void Dispose()
        {
            // The order is important: first we free the projector, then the text model
            VisionContext?.Dispose();
            VisionLock?.Dispose();
            TextModel.Dispose();
        }
    }
}