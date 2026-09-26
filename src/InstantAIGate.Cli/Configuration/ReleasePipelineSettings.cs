using System;
using System.Collections.Generic;
using System.Text;

namespace InstantAIGate.Cli.Configuration
{
    public class ReleasePipelineSettings
    {
        public string TargetBranch { get; set; } = "main";
        public string ReleaseBranchPrefix { get; set; } = "release-prep/v";
        public string AiModelId { get; set; } = "qwen3-vl-8b-instruct";
        public string[] AllowedBranchPrefixes { get; set; } = { "dev", "feat/", "fix/", "hotfix/", "rc/", "rfc/" };
    }
}
