using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace InstantAIGate.Infrastructure.Inference.Facades
{
    public interface IEmbeddingEngineFacade
    {
        Task<IReadOnlyList<float[]>> GetEmbeddingsAsync(List<string> inputs, InferenceSettings settings, CancellationToken ct = default);
    }
}