using System;
using System.Collections.Generic;
using System.Text;

namespace InstantAIGate.Domain.Entities
{
    /// <summary>
    /// Represents a specific hardware or quantization variant of an AI model.
    /// </summary>
    public class ModelVariant
    {
        /// <summary>
        /// Gets or sets the unique identifier for this variant.
        /// </summary>
        public string VariantId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the relative directory path where the files for this variant are located.
        /// </summary>
        public string TargetDirectoryPath { get; set; } = string.Empty;
    }
}