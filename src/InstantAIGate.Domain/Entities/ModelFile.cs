namespace InstantAIGate.Domain.Entities
{
    /// <summary>
    /// Represents an individual binary segment (shard) of an AI model layout.
    /// </summary>
    public record ModelFile(string FileName, string Url, long SizeBytes);
}

