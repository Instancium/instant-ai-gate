using System;
using System.Collections.Generic;
using System.Text;

namespace InstantAIGate.Infrastructure.ModelManagement
{
    public class HuggingFaceItem
    {
        public string type { get; set; } = string.Empty;
        public string path { get; set; } = string.Empty;
        public long size { get; set; }
    }
}
