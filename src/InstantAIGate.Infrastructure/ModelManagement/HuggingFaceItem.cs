using System;
using System.Collections.Generic;
using System.Text;

namespace InstantAIGate.Infrastructure.ModelManagement
{
    public class HuggingFaceItem
    {
        public string Type { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public long Size { get; set; }
    }
}
