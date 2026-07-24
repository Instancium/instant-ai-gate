// src/InstantAIGate.Domain/Entities/ModelManifest.cs
using InstantAIGate.Domain.Enums;
using InstantAIGate.Domain.ValueObjects;
using System;
using System.Collections.Generic;
using System.Linq;

namespace InstantAIGate.Domain.Entities
{
    /// <summary>
    /// Represents a comprehensive catalog manifest for a model, including metadata, provenance and file layout.
    /// </summary>
    public record ModelManifest
    {
        /// <summary>
        /// Logical repository identifier used across the system (for example, "provider/model-architecture-version").
        /// </summary>
        public string RepoId { get; init; }

        /// <summary>
        /// Human friendly display name. If empty, RepoId will be used as fallback.
        /// </summary>
        public string DisplayName { get; init; }

        /// <summary>
        /// Optional longer description of the model and its purpose.
        /// </summary>
        public string? Description { get; init; }

        /// <summary>
        /// Optional semantic version or release tag for the model.
        /// </summary>
        public string? Version { get; init; }

        /// <summary>
        /// Source repository information and provider metadata.
        /// </summary>
        public ModelRepository? Source { get; init; }

        /// <summary>
        /// License or usage terms for the model binaries.
        /// </summary>
        public string? License { get; init; }

        /// <summary>
        /// Collection of tags or categories associated with the model.
        /// </summary>
        public IReadOnlyList<string> Tags { get; init; }

        /// <summary>
        /// Binary file layout describing individual shards for the model.
        /// </summary>
        public IReadOnlyList<ModelFile> Files { get; init; }

        /// <summary>
        /// Optional checksums for each file to validate download integrity.
        /// </summary>
        public IReadOnlyList<ModelChecksum>? Checksums { get; init; }

        /// <summary>
        /// Model family/type classification used by runtime selection logic.
        /// </summary>
        public ModelType Type { get; init; } = ModelType.Llm;

        /// <summary>
        /// Total size in bytes across all declared files. Returns 0 if Files is empty.
        /// </summary>
        public long TotalSizeBytes => Files?.Sum(f => f.SizeBytes) ?? 0L;

        /// <summary>
        /// Creates a new ModelManifest instance.
        /// </summary>
        /// <param name="repoId">Logical repository identifier.</param>
        /// <param name="displayName">Human friendly name or empty to use repoId.</param>
        /// <param name="files">Collection of model files (must contain at least one file).</param>
        /// <param name="type">Model type classification.</param>
        public ModelManifest(string repoId, string displayName, IReadOnlyList<ModelFile> files, ModelType type = ModelType.Llm)
        {
            if (string.IsNullOrWhiteSpace(repoId)) throw new ArgumentException("repoId cannot be empty", nameof(repoId));
            if (files == null || files.Count == 0) throw new ArgumentException("files must contain at least one item", nameof(files));

            RepoId = repoId;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? repoId : displayName;
            Files = files;
            Tags = Array.Empty<string>();
            Type = type;
        }

        /// <summary>
        /// Parameterless constructor for serializers.
        /// </summary>
        public ModelManifest()
        {
            RepoId = string.Empty;
            DisplayName = string.Empty;
            Files = Array.Empty<ModelFile>();
            Tags = Array.Empty<string>();
            Checksums = null;
        }
    }
}