using InstantAIGate.Application.Dtos.Requests;
using InstantAIGate.Application.Interfaces.Inference;
using System.Runtime.CompilerServices;

namespace InstantAIGate.Infrastructure.Inference.Adapters
{
    public class MultimodalChatAdapter : IChatAdapter
    {
        public Task<string> GenerateAsync(LlamaChatRequest request, CancellationToken ct = default)
        {
            throw new NotImplementedException("Vision pipeline is under construction.");
        }

        public async IAsyncEnumerable<string> StreamAsync(LlamaChatRequest request, [EnumeratorCancellation] CancellationToken ct = default)
        {
            throw new NotImplementedException("Vision pipeline is under construction.");
            yield break;
        }
    }
}