using System;
using System.Collections.Generic;
using System.Text;

namespace InstantAIGate.Application.Interfaces.Inference
{
    // src/InstantAIGate.Application/Interfaces/Inference/IInferenceBackend.cs
    using System.Threading;
    using System.Threading.Channels;
    using System.Threading.Tasks;

    namespace InstantAIGate.Application.Interfaces.Inference
    {
        /// <summary>
        /// Defines a strict contract for inference execution, decoupling domain logic from specific hardware or tensor libraries.
        /// </summary>
        public interface IInferenceBackend
        {
            /// <summary>
            /// Enqueues a sequence of tokens for evaluation and streams the generated results back via a channel.
            /// </summary>
            /// <param name="requestId">Unique identifier for the inference request.</param>
            /// <param name="tokens">Input token arrays prepared by the BPE tokenizer.</param>
            /// <param name="writer">Channel writer to stream output tokens as they are generated.</param>
            /// <param name="ct">Cancellation token for the operation.</param>
            Task ProcessInferenceAsync(string requestId, int[] tokens, ChannelWriter<int> writer, CancellationToken ct = default);
        }
    }
}
