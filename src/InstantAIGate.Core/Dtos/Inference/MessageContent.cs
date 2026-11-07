using System.Text.Json.Serialization;

namespace InstantAIGate.Core.Dtos.Inference
{
    /// <summary>
    /// Polymorphic base type for multimodal message content to enable robust JSON deserialization via SignalR.
    /// </summary>
    [JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
    [JsonDerivedType(typeof(TextContent), "text")]
    [JsonDerivedType(typeof(ImageBase64Content), "image_base64")]
    [JsonDerivedType(typeof(ImageFileContent), "image_file")]
    [JsonDerivedType(typeof(ImageUrlContent), "image_url")]
    public abstract record MessageContent([property: JsonIgnore] string Type);
}
