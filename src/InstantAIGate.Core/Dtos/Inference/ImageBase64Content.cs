using System;
using System.Collections.Generic;
using System.Text;

namespace InstantAIGate.Core.Dtos.Inference
{
    public record ImageBase64Content(string Base64, string MediaType) : MessageContent("image_base64");
}
