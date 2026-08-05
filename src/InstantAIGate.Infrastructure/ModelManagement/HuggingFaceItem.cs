using System.Text.Json.Serialization;

namespace InstantAIGate.Infrastructure.ModelManagement
{
    public class HuggingFaceItem
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("path")]
        public string Path { get; set; } = string.Empty;

        [JsonPropertyName("size")]
        public long Size { get; set; }
    }
}