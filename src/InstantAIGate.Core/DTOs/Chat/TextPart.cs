using InstantAIGate.Core.Enums.Chat;
using System;
using System.Collections.Generic;
using System.Text;

namespace InstantAIGate.Core.DTOs.Chat
{
    public record TextPart : MessagePart
    {
        public string Text { get; init; } = string.Empty;
        public TextPart() { Type =  MessagePartType.Text; }
    }
}
