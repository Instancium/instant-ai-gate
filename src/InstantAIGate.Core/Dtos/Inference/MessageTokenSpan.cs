using System;
using System.Collections.Generic;
using System.Text;

namespace InstantAIGate.Core.Dtos.Inference
{
    public record MessageTokenSpan
    {
        public string Role { get; set; }
        public int StartPos { get; set; }
        public int EndPos { get; set; }

        public MessageTokenSpan(string role, int startPos, int endPos)
        {
            Role = role;
            StartPos = startPos;
            EndPos = endPos;
        }
    }
}
