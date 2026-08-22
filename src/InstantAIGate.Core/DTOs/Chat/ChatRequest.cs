using System;
using System.Collections.Generic;
using System.Text;

namespace InstantAIGate.Core.DTOs.Chat
{
    public record ChatRequest
    {
        /// <summary>
        /// ID of the model to use. Must match ModelManifest.Id.
        /// </summary>
        public string Model { get; init; } = string.Empty;

        /// <summary>
        /// A list of messages comprising the conversation so far.
        /// </summary>
        public IReadOnlyList<ChatMessage> Messages { get; init; } = [];

        /// <summary>
        /// What sampling temperature to use, between 0.0 and 2.0.
        /// </summary>
        public float? Temperature { get; init; }

        /// <summary>
        /// The maximum number of tokens to generate in the chat completion.
        /// </summary>
        public int? MaxTokens { get; init; }

        /// <summary>
        /// If true, partial message deltas will be sent using Server-Sent Events (SSE).
        /// </summary>
        public bool Stream { get; init; }
    }
}
