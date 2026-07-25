// src/InstantAIGate.Application/Interfaces/Inference/IInferenceBackend.cs
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;


namespace InstantAIGate.Application.Interfaces.Inference
{
    public interface IInferenceBackend
    {
        Task ExecuteInferenceAsync(string requestId, int[] tokens, ChannelWriter<int> responseWriter, CancellationToken cancellationToken);
    }
}

