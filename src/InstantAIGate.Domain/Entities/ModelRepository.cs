namespace InstantAIGate.Domain.Entities
{
    /// <summary>
    /// Represents the external source/provider information for a model repository.
    /// </summary>
    public record ModelRepository
    {
        /// <summary>
        /// Provider name (for example, "HuggingFace" or "ModelScope").
        /// </summary>
        public string ProviderName { get; init; } = string.Empty;

        /// <summary>
        /// Public repository URL where the model is hosted.
        /// </summary>
        public string RepoUrl { get; init; } = string.Empty;

        /// <summary>
        /// Optional author or maintainer name.
        /// </summary>
        public string? Author { get; init; }

        /// <summary>
        /// When true, indicates the repository is an official upstream release.
        /// </summary>
        public bool Official { get; init; }

        public ModelRepository() { }

        public ModelRepository(string providerName, string repoUrl, string? author = null, bool official = false)
        {
            ProviderName = providerName ?? string.Empty;
            RepoUrl = repoUrl ?? string.Empty;
            Author = author;
            Official = official;
        }
    }
}
