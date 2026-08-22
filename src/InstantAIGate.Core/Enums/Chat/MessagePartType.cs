using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace InstantAIGate.Core.Enums.Chat
{
    /// <summary>
    /// Specifies the type of content part in a chat message.
    /// Serialized as a string to match OpenAI API specifications.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum MessagePartType
    {
        Text,

        [JsonPropertyName("image_url")]
        ImageUrl,

        Audio
    }
}
