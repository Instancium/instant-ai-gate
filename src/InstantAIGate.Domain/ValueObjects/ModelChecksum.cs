namespace InstantAIGate.Domain.ValueObjects
{
    /// <summary>
    /// Value object representing a cryptographic checksum for a model file.
    /// </summary>
    public record ModelChecksum
    {
        /// <summary>
        /// File name this checksum relates to.
        /// </summary>
        public string FileName { get; init; } = string.Empty;

        /// <summary>
        /// Algorithm used for the checksum (for example, "SHA256").
        /// </summary>
        public string Algorithm { get; init; } = "SHA256";

        /// <summary>
        /// Hex encoded hash string.
        /// </summary>
        public string Hash { get; init; } = string.Empty;

        public ModelChecksum() { }

        public ModelChecksum(string fileName, string hash, string algorithm = "SHA256")
        {
            FileName = fileName ?? string.Empty;
            Hash = hash ?? string.Empty;
            Algorithm = algorithm ?? "SHA256";
        }
    }
}
