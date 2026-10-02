namespace InstantAIGate.Core.Interfaces.Inference;

using InstantAIGate.Core.Dtos.Config;
using InstantAIGate.Core.Dtos.Inference;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public interface IInferenceEngine
{
    Task<int[]> TokenizeDataAsync(string modelId, string text, CancellationToken ct = default);
    Task<string> ApplyChatTemplateAsync(string modelId, IEnumerable<ChatMessage> messages, CancellationToken ct = default);

    IAsyncEnumerable<string> StreamDeltaGenerationAsync(
        string sessionId,
        ChatMessage deltaMessage,
        InferenceSettings? overrideSettings = null,
        CancellationToken ct = default);
}