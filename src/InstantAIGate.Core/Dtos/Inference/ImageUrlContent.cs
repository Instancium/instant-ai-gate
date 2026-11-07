namespace InstantAIGate.Core.Dtos.Inference
{
    /// <summary>
    /// Represents image content referenced by a URL.
    /// </summary>
    public record ImageUrlContent(string Url) : MessageContent("image_url");
}
