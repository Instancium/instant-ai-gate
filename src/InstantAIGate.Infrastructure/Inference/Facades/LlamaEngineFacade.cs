using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace InstantAIGate.Infrastructure.Inference.Facades
{
    public class InferenceSettings
    {
        public string ModelId { get; set; } = string.Empty;
        public float Temperature { get; set; } = 0.7f;
        public int TopK { get; set; } = 40;
        public float TopP { get; set; } = 0.9f;
        public int MaxTokens { get; set; } = 512;
        public int BatchSize { get; set; } = 512;
        public uint? Seed { get; set; }
    }

    public interface ILlamaEngineFacade
    {
        Task<int> GetTokenCountAsync(string modelId, string text, CancellationToken ct = default);
        IAsyncEnumerable<string> StreamGenerationAsync(string prompt, InferenceSettings settings, CancellationToken ct = default);
    }

    /// <summary>
    /// Temporary placeholder to ensure successful project compilation
    /// during the debugging of model and UI loading. 
    /// </summary>
    public partial class LlamaEngineFacade(ModelManager _modelManager) : ILlamaEngineFacade
    {
        public Task<int> GetTokenCountAsync(string modelId, string text, CancellationToken ct = default)
        {
            return Task.FromResult(0);
        }

        public async IAsyncEnumerable<string> StreamGenerationAsync(
            string prompt,
            InferenceSettings settings,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            yield return "STUB: Инференс временно отключен на период отладки загрузки моделей.";
            await Task.CompletedTask;
        }
    }
}