using InstantAIGate.Application.Dtos.Requests;

namespace InstantAIGate.Application.Interfaces.Inference
{
    public interface IChatAdapter
    {
        Task<string> GenerateAsync(ChatRequest request, CancellationToken ct = default);
        IAsyncEnumerable<string> StreamAsync(ChatRequest request, CancellationToken ct = default);
    }
}
