using InstantAIGate.Core.Abstractions.Chat;
using InstantAIGate.Core.DTOs.Common;
using InstantAIGate.Runners.Onnx.Adapters;
using System;
using System.IO;
using System.Text.Json;

namespace InstantAIGate.Runners.Onnx.Adapters
{
    public class OnnxChatAdapterFactory : IChatAdapterFactory
    {
        public IChatAdapter CreateAdapter(ModelManifest manifest)
        {
            if (manifest == null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }

            bool isMultimodal = DetermineIfMultimodal(manifest);

            if (isMultimodal)
            {
                return new MultiModalAdapter();
            }
            else
            {
                return new TextAdapter();
            }
        }

        /// <summary>
        /// Analyzes the manifest and the model's physical configuration file 
        /// to determine if it supports vision/audio inputs.
        /// </summary>
        private bool DetermineIfMultimodal(ModelManifest manifest)
        {
            // 1. Explicit override via Metadata has the highest priority
            if (manifest.Metadata != null &&
                manifest.Metadata.TryGetValue("IsMultimodal", out var isMultimodalStr) &&
                bool.TryParse(isMultimodalStr, out bool isMultimodal))
            {
                return isMultimodal;
            }

            // 2. Read the actual ONNX GenAI configuration file natively
            string configPath = Path.Combine(manifest.Path, "genai_config.json");

            if (File.Exists(configPath))
            {
                try
                {
                    // Read file quickly without locking
                    string jsonContent = File.ReadAllText(configPath);
                    using JsonDocument doc = JsonDocument.Parse(jsonContent);

                    // Typical ONNX GenAI config structure: { "model": { "type": "phi3v" } }
                    if (doc.RootElement.TryGetProperty("model", out JsonElement modelElement) &&
                        modelElement.TryGetProperty("type", out JsonElement typeElement))
                    {
                        string? modelType = typeElement.GetString()?.ToLowerInvariant();

                        if (!string.IsNullOrEmpty(modelType))
                        {
                            return IsMultimodalModelType(modelType);
                        }
                    }
                }
                catch
                {
                    // Fallback to name heuristics if JSON parsing fails
                }
            }

            // 3. Absolute fallback: analyze the manifest ID if config is missing
            return IsMultimodalModelType(manifest.Id.ToLowerInvariant());
        }

        /// <summary>
        /// Contains the hardcoded list of known multimodal model types in ONNX.
        /// </summary>
        private bool IsMultimodalModelType(string typeIdentifier)
        {
            return typeIdentifier.Contains("phi3v") ||
                   typeIdentifier.Contains("phi4mm") ||
                   typeIdentifier.Contains("qwen2_5_vl") ||
                   typeIdentifier.Contains("qwen3_vl") ||
                   typeIdentifier.Contains("llava") ||
                   typeIdentifier.Contains("vision");
        }
    }
}