using System;
using System.Collections.Generic;
using System.Text;

namespace InstantAIGate.Core.Dtos.Inference
{
    public record ImageFileContent(string FilePath) : MessageContent("image_file");
}
