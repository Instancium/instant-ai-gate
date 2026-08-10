using InstantAIGate.Application.Adapters;
using Microsoft.ML.OnnxRuntimeGenAI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace InstantAIGate.Infrastructure.Adapters
{
    public class MultiModalAdapter : IGenAiAdapter
    {
        private Model _model;
        private Tokenizer _tokenizer;
        private MultiModalProcessor _processor;
        private string _chatTemplate;

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
                }

                _model = new Model(config);
                _tokenizer = new Tokenizer(_model);
                _processor = new MultiModalProcessor(_model);

                var jinjaPath = Path.Combine(modelDirectory, "chat_template.jinja");
                if (File.Exists(jinjaPath))
                {
                    _chatTemplate = File.ReadAllText(jinjaPath);
                }

                _isLoaded = true;
            }
        }

        public async IAsyncEnumerable<string> GenerateStreamAsync(
            string prompt,
            IReadOnlyList<string> imagePaths,
            IReadOnlyList<string> audioPaths,
            int maxLength,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (!_isLoaded)
            {
                throw new InvalidOperationException("Adapter is not initialized. Call Initialize() first.");
            }

            Images images = null;
            Audios audios = null;
            NamedTensors inputTensors = null;

            try
            {
                using var generatorParams = new GeneratorParams(_model);
                generatorParams.SetSearchOption("max_length", maxLength);

                int numImages = 0;
                if (imagePaths != null && imagePaths.Any())
                {
                    images = Images.Load(imagePaths.ToArray());
                    numImages = imagePaths.Count;
                }

                int numAudios = 0;
                if (audioPaths != null && audioPaths.Any())
                {
                    audios = Audios.Load(audioPaths.ToArray());
                    numAudios = audioPaths.Count;
                }

                string contentWithMediaTags = InjectMediaTags(_model.GetModelType(), prompt, numImages, numAudios);

                var messagesArray = new[]
                {
                    new { role = "user", content = contentWithMediaTags }
                };
                string messagesJson = JsonSerializer.Serialize(messagesArray);

                string templateString = string.IsNullOrEmpty(_chatTemplate) ? string.Empty : _chatTemplate;
                string toolsString = string.Empty;

                string finalPrompt = _tokenizer.ApplyChatTemplate(
                    templateString,
                    messagesJson,
                    toolsString,
                    true);

                using var generator = new Generator(_model, generatorParams);

                inputTensors = _processor.ProcessImagesAndAudios(finalPrompt, images, audios);
                generator.SetInputs(inputTensors);

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
            finally
            {
                images?.Dispose();
                audios?.Dispose();
                inputTensors?.Dispose();
            }
        }

        private string InjectMediaTags(string modelType, string rawPrompt, int numImages, int numAudios)
        {
            if (numImages == 0 && numAudios == 0)
            {
                return rawPrompt;
            }

            string content = string.Empty;

            if (modelType == "phi3v" || modelType == "phi4mm")
            {
                for (int i = 0; i < numImages; i++)
                {
                    content += $"<|image_{i + 1}|>\n";
                }
                for (int i = 0; i < numAudios; i++)
                {
                    content += $"<|audio_{i + 1}|>\n";
                }
                content += rawPrompt;
            }
            else if (modelType == "qwen2_5_vl" || modelType == "qwen3_vl" || modelType == "fara")
            {
                for (int i = 0; i < numImages; i++)
                {
                    content += "<|vision_start|><|image_pad|><|vision_end|>";
                }
                content += rawPrompt;
            }
            else
            {
                content = rawPrompt;
            }

            return content;
        }

        public void Dispose()
        {
            _processor?.Dispose();
            _tokenizer?.Dispose();
            _model?.Dispose();
        }
    }
}