using InstantAIGate.Application.Adapters;
using Microsoft.ML.OnnxRuntimeGenAI;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace InstantAIGate.Infrastructure.Adapters
{
    public class TextAdapter : IGenAiAdapter
    {
        private Model? _model;
        private Tokenizer? _tokenizer;
        private bool _isLoaded;
        private readonly object _lockObject = new object();

        public void Initialize(string modelDirectory, string executionProvider = "cpu")
        {
            lock (_lockObject)
            {
                if (_isLoaded)
                {
                    return;
                }

                var config = new Config(modelDirectory);
                config.ClearProviders();

                if (!string.Equals(executionProvider, "cpu", StringComparison.OrdinalIgnoreCase))
                {
                    config.AppendProvider(executionProvider);

                    if (string.Equals(executionProvider, "dml", StringComparison.OrdinalIgnoreCase))
                    {
                        config.Overlay("{\"past_present_share_buffer\": true}");
                    }
                }

                _model = new Model(config);
                _tokenizer = new Tokenizer(_model);
                _isLoaded = true;
            }
        }

        public async IAsyncEnumerable<string> GenerateStreamAsync(
            string prompt,
            IReadOnlyList<string>? imagePaths,
            IReadOnlyList<string>? audioPaths,
            int maxLength,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (!_isLoaded || _model == null || _tokenizer == null)
            {
                throw new InvalidOperationException("Adapter is not initialized.");
            }

            if ((imagePaths != null && imagePaths.Count > 0) || (audioPaths != null && audioPaths.Count > 0))
            {
                throw new NotSupportedException("This adapter does not support multimodal inputs.");
            }

            using var generatorParams = new GeneratorParams(_model);
            generatorParams.SetSearchOption("max_length", maxLength);
            generatorParams.SetSearchOption("repetition_penalty", 1.15f);
            generatorParams.SetSearchOption("temperature", 0.7f);
            generatorParams.SetSearchOption("top_p", 0.9f);

            using var generator = new Generator(_model, generatorParams);

            using var tokens = _tokenizer.Encode(prompt);
            generator.AppendTokenSequences(tokens);

            using var tokenizerStream = _tokenizer.CreateStream();

            while (!generator.IsDone())
            {
                cancellationToken.ThrowIfCancellationRequested();

                await Task.Run(() => generator.GenerateNextToken(), cancellationToken);

                var nextTokens = generator.GetNextTokens();
                if (nextTokens.Length > 0)
                {
                    string decodedToken = tokenizerStream.Decode(nextTokens[0]);
                    yield return decodedToken;
                }
            }
        }

        public void Dispose()
        {
            _tokenizer?.Dispose();
            _model?.Dispose();
        }
    }
}