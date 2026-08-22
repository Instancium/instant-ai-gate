using InstantAIGate.Core.Enums.Chat;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace InstantAIGate.Core.DTOs.Chat
{
    /// <summary>
    /// Base abstract class for polymorphic JSON deserialization.
    /// Derived types are automatically instantiated based on the "type" property.
    /// </summary>
    [JsonDerivedType(typeof(TextPart), typeDiscriminator: "text")]
    [JsonDerivedType(typeof(ImageUrlPart), typeDiscriminator: "image_url")]
    public abstract record MessagePart
    {
        public MessagePartType Type { get; init; }
    }
}
