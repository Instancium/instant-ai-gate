using System;
using System.Collections.Generic;
using System.Text;

namespace InstantAIGate.Infrastructure.Inference.Context
{
    /// <summary>
    /// Encapsulates the runtime context for an inference session, providing access 
    /// to both standard text operations and optional multimodal processing.
    /// </summary>
    public sealed class InferenceContext : IDisposable
    {
        public ModelContext TextContext { get; }
        public VisionContext? VisionContext { get; }

        public bool HasVisionSupport => VisionContext != null;

        public InferenceContext(ModelContext textContext, VisionContext? visionContext = null)
        {
            TextContext = textContext ?? throw new ArgumentNullException(nameof(textContext));
            VisionContext = visionContext;
        }

        public void Dispose()
        {
            // TextContext controls the semaphore release and object pooling.
            // VisionContext is typically shared per loaded model and is disposed only during model unload.
            TextContext.Dispose();
        }
    }
}
