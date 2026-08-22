using System.Collections.Generic;

namespace InstantAIGate.Core.DTOs.Common
{
    public record ModelManifest
    {
        /// <summary>
        /// Unique identifier for the API routing (e.g., "qwen3-vl-8b").
        /// </summary>
        public string Id { get; init; } = string.Empty;

        /// <summary>
        /// Human-readable name for UI rendering (e.g., "Qwen 3 Vision Large").
        /// </summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>
        /// General description of the model capabilities.
        /// </summary>
        public string Description { get; init; } = string.Empty;

        /// <summary>
        /// Physical path to the model directory/file, or a remote URI.
        /// </summary>
        public string Path { get; init; } = string.Empty;

        /// <summary>
        /// Custom engine-specific configuration (e.g., hardware accelerators).
        /// </summary>
        public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
    }
}