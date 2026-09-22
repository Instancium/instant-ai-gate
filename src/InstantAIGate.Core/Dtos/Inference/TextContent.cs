using System;
using System.Collections.Generic;
using System.Text;

namespace InstantAIGate.Core.Dtos.Inference
{
    public record TextContent(string Text) : MessageContent("text");
}
