using InstantAIGate.Core.Enums.Chat;
using System;
using System.Collections.Generic;
using System.Text;

namespace InstantAIGate.Core.DTOs.Chat
{
    public record ImageUrlPart : MessagePart
    {
        public ImageUrl ImageUrl { get; init; } = new();
        public ImageUrlPart()
        {
            Type = MessagePartType.ImageUrl;
        }
    }
}
