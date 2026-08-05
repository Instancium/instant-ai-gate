using System;
using System.Collections.Generic;
using System.Text;

namespace InstantAIGate.Domain.Entities
{
    public class SupportedModelDefinition 
    {
        public string RepoId { get; set; } = string.Empty;
        public string TargetDirectoryPath { get; set; } = string.Empty;
        public Enums.ModelSourceProvider SourceProvider { get; set; }
        public Enums.ModelTier Tier { get; set; }
    }
}
