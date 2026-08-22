using InstantAIGate.Core.Abstractions.Chat;
using InstantAIGate.Core.DTOs.Chat;
using InstantAIGate.Core.DTOs.Common;
using Microsoft.ML.OnnxRuntimeGenAI;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace InstantAIGate.Runners.Onnx.Adapters
{
    public class TextAdapter : IChatAdapter
    {
        private Model? _model;
        private Tokenizer? _tokenizer;
        private string? _chatTemplate;

        private bool _isLoaded;
        private readonly SemaphoreSlim _initLock = new SemaphoreSlim(1, 1);

        public bool SupportsMultimodal => false;

        public async Task InitializeAsync(ModelManifest manifest, CancellationToken cancellationToken = default)
        {
            if (_isLoaded)
            {
                return;
            }

            await _initLock.WaitAsync(cancellationToken);
            try
            {
                if (_isLoaded)
                {
                    return;
                }

                var config = new Config(manifest.Path);
                config.ClearProviders();

                string executionProvider = GetExecutionProvider(manifest);

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

                var jinjaPath = Path.Combine(manifest.Path, "chat_template.jinja");
                if (File.Exists(jinjaPath))
                {
                    _chatTemplate = await File.ReadAllTextAsync(jinjaPath, cancellationToken);
                }

                _isLoaded = true;
            }
            finally
            {
                _initLock.Release();
            }
        }

        public async IAsyncEnumerable<string> GenerateStreamAsync(
            ChatRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (!_isLoaded || _model == null || _tokenizer == null)
            {
                throw new InvalidOperationException("TextAdapter is not initialized. Call InitializeAsync() first.");
            }

            var messagesList = new List<object>();

            foreach (var message in request.Messages)
            {
                var textBuilder = new StringBuilder();
                foreach (var part in message.ContentParts)
                {
                    if (part is TextPart textPart)
                    {
                        textBuilder.AppendLine(textPart.Text);
                    }
                    else
                    {
                        throw new NotSupportedException("This model does not support multimodal inputs. Remove images and audio from the request.");
                    }
                }

                messagesList.Add(new { role = message.Role, content = textBuilder.ToString().TrimEnd() });
            }

            string messagesJson = JsonSerializer.Serialize(messagesList);
            string templateString = string.IsNullOrEmpty(_chatTemplate) ? string.Empty : _chatTemplate;

            string finalPrompt = _tokenizer.ApplyChatTemplate(templateString, messagesJson, string.Empty, true);

            using var generatorParams = new GeneratorParams(_model);

            int maxLength = request.MaxTokens ?? 2048;
            generatorParams.SetSearchOption("max_length", maxLength);
            generatorParams.SetSearchOption("repetition_penalty", 1.05f);

            if (request.Temperature.HasValue)
            {
                generatorParams.SetSearchOption("temperature", request.Temperature.Value);
            }

            using var generator = new Generator(_model, generatorParams);
            using var tokens = _tokenizer.Encode(finalPrompt);

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

        /// <summary>
        /// Determines the appropriate execution provider by first inspecting the model's directory path.
        /// Falls back to the Metadata dictionary if the path is ambiguous.
        /// </summary>
        private string GetExecutionProvider(ModelManifest manifest)
        {
            if (!string.IsNullOrWhiteSpace(manifest.Path))
            {
                string normalizedPath = manifest.Path.Replace('\\', '/').ToLowerInvariant();

                if (normalizedPath.Contains("/cuda") || normalizedPath.Contains("cuda-"))
                    return "cuda";

                if (normalizedPath.Contains("/dml") || normalizedPath.Contains("/directml") || normalizedPath.Contains("dml-"))
                    return "dml";

                if (normalizedPath.Contains("/rocm") || normalizedPath.Contains("rocm-"))
                    return "rocm";

                if (normalizedPath.Contains("/cpu") || normalizedPath.Contains("cpu-"))
                    return "cpu";
            }

            if (manifest.Metadata != null && manifest.Metadata.TryGetValue("ExecutionProvider", out var epOverride))
            {
                if (!string.IsNullOrWhiteSpace(epOverride))
                {
                    return epOverride;
                }
            }

            return "cpu";
        }

        public void Dispose()
        {
            _tokenizer?.Dispose();
            _model?.Dispose();
            _initLock.Dispose();
        }
    }
}