using InstantAIGate.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace InstantAIGate.Domain.Entities
{
    /// <summary>
    /// Represents the base definition of an AI model supported by the application.
    /// </summary>
    public class SupportedModelDefinition
    {
        /// <summary>
        /// Gets or sets the repository identifier for the model.
        /// </summary>
        public string RepoId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the source provider responsible for hosting the model files.
        /// </summary>
        public ModelSourceProvider SourceProvider { get; set; }

        /// <summary>
        /// Gets or sets the access tier classification for the model.
        /// </summary>
        public ModelTier Tier { get; set; }

        /// <summary>
        /// Gets or sets the collection of specific hardware or quantization variants available for this model.
        /// </summary>
        public List<ModelVariant> Variants { get; set; } = new List<ModelVariant>();
    }
}