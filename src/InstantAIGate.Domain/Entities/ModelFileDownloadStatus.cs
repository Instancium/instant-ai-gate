namespace InstantAIGate.Domain.Entities
{
    /// <summary>
    /// Represents domain-level download progress information for an individual model file shard.
    /// </summary>
    public class ModelFileDownloadStatus
    {
        /// <summary>
        /// Logical repository identifier associated with the file.
        /// </summary>
        public string RepoId { get; set; } = string.Empty;

        /// <summary>
        /// The file name of the shard (for example, "model-00001-of-00005.gguf").
        /// </summary>
        public string FileName { get; set; } = string.Empty;

        /// <summary>
        /// Declared total number of bytes expected for the file.
        /// </summary>
        public long TotalBytes { get; set; }

        /// <summary>
        /// Current number of bytes successfully downloaded to disk.
        /// </summary>
        public long BytesDownloaded { get; set; }

        /// <summary>
        /// Percentage completion value between 0.0 and 100.0.
        /// </summary>
        public double Percent => TotalBytes <= 0 ? 0.0 : (double)BytesDownloaded / TotalBytes * 100.0;

        /// <summary>
        /// True when the file download is finished and persisted.
        /// </summary>
        public bool IsCompleted => BytesDownloaded >= TotalBytes && TotalBytes > 0;

        /// <summary>
        /// Last time the progress was updated.
        /// </summary>
        public DateTimeOffset LastUpdated { get; set; } = DateTimeOffset.UtcNow;

        /// <summary>
        /// Default constructor for serializers.
        /// </summary>
        public ModelFileDownloadStatus() { }

        /// <summary>
        /// Creates a new instance with initial values.
        /// </summary>
        public ModelFileDownloadStatus(string repoId, string fileName, long totalBytes)
        {
            RepoId = repoId ?? string.Empty;
            FileName = fileName ?? string.Empty;
            TotalBytes = totalBytes;
            BytesDownloaded = 0;
            LastUpdated = DateTimeOffset.UtcNow;
        }
    }
}
