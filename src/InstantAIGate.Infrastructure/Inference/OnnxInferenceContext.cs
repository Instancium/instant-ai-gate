using Microsoft.ML.OnnxRuntimeGenAI;
using System;

namespace InstantAIGate.Infrastructure.Inference
{
    public sealed class OnnxInferenceContext : IDisposable
    {
        public Model ActiveModel { get; }
        public Tokenizer ActiveTokenizer { get; }
        private readonly Action _onDisposeCallback;

        public OnnxInferenceContext(Model activeModel, Tokenizer activeTokenizer, Action onDisposeCallback)
        {
            ActiveModel = activeModel ?? throw new ArgumentNullException(nameof(activeModel));
            ActiveTokenizer = activeTokenizer ?? throw new ArgumentNullException(nameof(activeTokenizer));
            _onDisposeCallback = onDisposeCallback ?? throw new ArgumentNullException(nameof(onDisposeCallback));
        }

        public void Dispose()
        {
            _onDisposeCallback.Invoke();
        }
    }
}