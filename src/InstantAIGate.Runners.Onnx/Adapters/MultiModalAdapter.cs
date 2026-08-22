using InstantAIGate.Core.Abstractions.Chat;
using InstantAIGate.Core.Enums.Chat;
using InstantAIGate.Core.DTOs.Chat;
using InstantAIGate.Core.DTOs.Common;
using Microsoft.ML.OnnxRuntimeGenAI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace InstantAIGate.Runners.Onnx.Adapters
{
    public class MultiModalAdapter : IChatAdapter
    {
        private Model? _model;
        private Tokenizer? _tokenizer;
        private MultiModalProcessor? _processor;
        private string? _chatTemplate;
        private string _modelType = string.Empty;

        private bool _isLoaded;
        private readonly SemaphoreSlim _initLock = new SemaphoreSlim(1, 1);

        public bool SupportsMultimodal => true;


        public async Task InitializeAsync(ModelManifest manifest, CancellationToken cancellationToken = default)
        {
            if (_isLoaded) return;

            await _initLock.WaitAsync(cancellationToken);
            try
            {
                if (_isLoaded) return;

                var config = new Config(manifest.Path);
                config.ClearProviders();

                // Get the provider using our new intelligent helper
                string executionProvider = GetExecutionProvider(manifest);

                // Apply the provider if it is not CPU
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
                _processor = new MultiModalProcessor(_model);

                _modelType = manifest.Id.ToLowerInvariant();

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
            if (!_isLoaded || _model == null || _tokenizer == null || _processor == null)
            {
                throw new InvalidOperationException("Adapter is not initialized.");
            }

            var tempImagePaths = new List<string>();
            Images? images = null;
            NamedTensors? inputTensors = null;

            try
            {
                var promptBuilder = new StringBuilder();
                int numImages = 0;
                string currentRole = "user";

                foreach (var message in request.Messages)
                {
                    currentRole = message.Role;
                    foreach (var part in message.ContentParts)
                    {
                        if (part is TextPart textPart)
                        {
                            promptBuilder.AppendLine(textPart.Text);
                        }
                        else if (part is ImageUrlPart imagePart)
                        {
                            numImages++;
                            string tempPath = await PrepareImagePathAsync(imagePart.ImageUrl.Url, cancellationToken);
                            tempImagePaths.Add(tempPath);
                        }
                    }
                }

                if (tempImagePaths.Any())
                {
                    images = Images.Load(tempImagePaths.ToArray());
                }

                string rawPrompt = promptBuilder.ToString().TrimEnd();
                string contentWithMediaTags = InjectMediaTags(_modelType, rawPrompt, numImages, 0);

                var messagesArray = new[]
                {
                    new { role = currentRole, content = contentWithMediaTags }
                };

                string messagesJson = JsonSerializer.Serialize(messagesArray);
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
                inputTensors = _processor.ProcessImages(finalPrompt, images);

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
                inputTensors?.Dispose();
                CleanupTempFiles(tempImagePaths);
            }
        }

        /// <summary>
        /// Determines the appropriate execution provider by first inspecting the model's directory path.
        /// Falls back to the Metadata dictionary if the path is ambiguous.
        /// </summary>
        private string GetExecutionProvider(ModelManifest manifest)
        {
            // 1. Try to infer from the directory path structure (e.g., .../onnxruntime/cuda/...)
            if (!string.IsNullOrWhiteSpace(manifest.Path))
            {
                // Normalize path separators and case for safe searching
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

            // 2. Fallback to explicit Metadata configuration if path inference failed
            if (manifest.Metadata != null && manifest.Metadata.TryGetValue("ExecutionProvider", out var epOverride))
            {
                if (!string.IsNullOrWhiteSpace(epOverride))
                {
                    return epOverride;
                }
            }

            // 3. Absolute fallback
            return "cpu";
        }

        private async Task<string> PrepareImagePathAsync(string imageUrl, CancellationToken cancellationToken)
        {
        
            if (imageUrl.StartsWith("data:image", StringComparison.OrdinalIgnoreCase))
            {
                var commaIndex = imageUrl.IndexOf(',');
                if (commaIndex > -1)
                {
                  
                    string header = imageUrl.Substring(0, commaIndex);
                    string extension = ".png"; 
                    if (header.Contains("jpeg", StringComparison.OrdinalIgnoreCase) || header.Contains("jpg", StringComparison.OrdinalIgnoreCase))
                        extension = ".jpg";
                    else if (header.Contains("webp", StringComparison.OrdinalIgnoreCase))
                        extension = ".webp";

                    string tempFilePath = Path.ChangeExtension(Path.GetTempFileName(), extension);
                    var base64Data = imageUrl.Substring(commaIndex + 1);
                    byte[] imageBytes = Convert.FromBase64String(base64Data);
                    await File.WriteAllBytesAsync(tempFilePath, imageBytes, cancellationToken);
                    return tempFilePath;
                }
            }

            if (imageUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                string extension = Path.GetExtension(new Uri(imageUrl).AbsolutePath);
                if (string.IsNullOrEmpty(extension)) extension = ".png";

                string tempFilePath = Path.ChangeExtension(Path.GetTempFileName(), extension);
                using var httpClient = new System.Net.Http.HttpClient();
                byte[] imageBytes = await httpClient.GetByteArrayAsync(imageUrl, cancellationToken);
                await File.WriteAllBytesAsync(tempFilePath, imageBytes, cancellationToken);
                return tempFilePath;
            }

     
            string cleanPath = imageUrl.Trim('"');
            if (File.Exists(cleanPath))
            {
                return cleanPath;
            }

            throw new FileNotFoundException($"Image file not found or unsupported format: {cleanPath}");
        }

        private void CleanupTempFiles(IEnumerable<string> filePaths)
        {
            string systemTempFolder = Path.GetTempPath();

            foreach (var path in filePaths)
            {
                try
                {
                    if (File.Exists(path) && path.StartsWith(systemTempFolder, StringComparison.OrdinalIgnoreCase))
                    {
                        File.Delete(path);
                    }
                }
                catch
                {
                    // Ignore exceptions during cleanup to avoid crashing the response pipeline
                }
            }
        }

        private string InjectMediaTags(string modelType, string rawPrompt, int numImages, int numAudios)
        {
            if (numImages == 0 && numAudios == 0)
            {
                return rawPrompt;
            }

            string content = string.Empty;

            if (modelType.Contains("phi3") || modelType.Contains("phi4"))
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
            else if (modelType.Contains("qwen"))
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
            _initLock.Dispose();
        }
    }
}