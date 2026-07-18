using InstantAIGate.Application.Dtos.Requests;

namespace InstantAIGate.Application.Interfaces.Inference
{
    public interface IChatAdapter
    {
        Task<string> GenerateAsync(LlamaChatRequest request, CancellationToken ct = default);
        IAsyncEnumerable<string> StreamAsync(LlamaChatRequest request, CancellationToken ct = default);
    }
}
