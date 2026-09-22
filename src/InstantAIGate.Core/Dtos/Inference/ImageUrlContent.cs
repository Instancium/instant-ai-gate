using System;
using System.Collections.Generic;
using System.Text;

namespace InstantAIGate.Core.Dtos.Inference
{
    public record ImageUrlContent(string Url) : MessageContent("image_url");
}
