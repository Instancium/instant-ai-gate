using InstantAIGate.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace InstantAIGate.Domain.Entities
{
    public class SupportedModelDefinition
    {
        public string RepoId { get; set; } = string.Empty;
        public ModelSourceProvider SourceProvider { get; set; }
        public ModelTier Tier { get; set; }
        public List<ModelVariant> Variants { get; set; } = new List<ModelVariant>();
    }
}
