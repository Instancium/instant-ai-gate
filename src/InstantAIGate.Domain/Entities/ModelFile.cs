namespace InstantAIGate.Domain.Entities
{
    /// <summary>
    /// Represents an individual binary segment (shard) of an AI model layout.
    /// </summary>
    public class ModelFile
    {
        public string RelativePath { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public string FileName => Path.GetFileName(RelativePath);


        public ModelFile() { }

        [Obsolete("This constructor is maintained for backward compatibility only. Please use the default constructor and object initialization, setting the RelativePath property instead.")]
        public ModelFile(string FileName, string Url, long SizeBytes)
        {
            this.Url = Url;
            this.SizeBytes = SizeBytes;
        }

    }
}

