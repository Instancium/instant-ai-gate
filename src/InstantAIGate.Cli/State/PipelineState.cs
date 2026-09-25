using System;
using System.Collections.Generic;
using System.Text;

namespace InstantAIGate.Cli.State
{
    public class PipelineState
    {
        public int LastCompletedStep { get; set; } = 0;
        public string SelectedBranch { get; set; } = string.Empty;
        public string NewVersion { get; set; } = string.Empty;
        public string ReleaseBranch { get; set; } = string.Empty;
        public string ReleaseNotes { get; set; } = string.Empty;
    }
}
