namespace InstantAIGate.Core.Dtos.Inference
{
    public record ImageBase64Content(string Base64, string MediaType) : MessageContent("image_base64");
}
