namespace InstantAIGate.Core.Dtos.Inference
{
    /// <summary>
    /// Represents base64 encoded image content with media type.
    /// </summary>
    public record ImageBase64Content(string Base64, string MediaType) : MessageContent("image_base64");
}
