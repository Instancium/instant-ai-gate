using InstantAIGate.Core.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace InstantAIGate.Core.Domain
{
    public class AiModel
    {
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public ModelEngineType Engine { get; set; }

        public ModelTaskCategory TaskCategory { get; set; }

        public string BaseDirectory { get; set; } = string.Empty;

        public HardwareAccelerator PreferredAccelerator { get; set; } = HardwareAccelerator.Cpu;

        public long MaxVramBytes { get; set; }

        public bool IsLoaded { get; private set; }

        public DateTime? LastUsedUtc { get; private set; }

        public Dictionary<string, string> ExecutionParameters { get; set; } = new Dictionary<string, string>();

        public void MarkAsLoaded()
        {
            IsLoaded = true;
            UpdateLastUsed();
        }

        public void MarkAsUnloaded()
        {
            IsLoaded = false;
        }

        public void UpdateLastUsed()
        {
            LastUsedUtc = DateTime.UtcNow;
        }
    }
}
