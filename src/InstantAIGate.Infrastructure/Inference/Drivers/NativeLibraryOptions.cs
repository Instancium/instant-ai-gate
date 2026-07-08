using System;
using System.Collections.Generic;
using System.Text;

namespace InstantAIGate.Infrastructure.Inference.Drivers
{

    /// <summary>
    /// Configuration options for native library loading and backend selection.
    /// </summary>
    [Obsolete("Configuration is now managed via environment variables and autodetection.", error: false)]
    public class NativeLibraryOptions
    {
        public const string SectionName = "NativeLibrary";

        /// <summary>
        /// Preferred backend: "auto", "cpu", "gpu", or specific backend name (e.g., "vulkan", "cuda").
        /// Default: "auto" (GPU if available, otherwise CPU).
        /// </summary>
        public string PreferredBackend { get; set; } = "cuda";

        /// <summary>
        /// Enable detailed debug logging for native library operations.
        /// </summary>
        public bool EnableDebugLogging { get; set; } = false;
    }
}
