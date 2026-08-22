using System;
using System.Collections.Generic;
using System.Text;

namespace InstantAIGate.Core.DTOs.Chat
{
    public record ChatMessage
    {
        /// <summary>
        /// The role of the message author (e.g., "system", "user", "assistant").
        /// </summary>
        public string Role { get; init; } = string.Empty;

        /// <summary>
        /// The content parts of the message. Handles both simple text and multimodal inputs.
        /// </summary>
        public IReadOnlyList<MessagePart> ContentParts { get; init; } = new List<MessagePart>();
    }
}
