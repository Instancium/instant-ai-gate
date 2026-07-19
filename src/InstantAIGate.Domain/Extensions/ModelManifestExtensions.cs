using InstantAIGate.Domain.Entities;

namespace InstantAIGate.Domain.Extensions
{
    public static class ModelManifestExtensions
    {
        /// <summary>
        /// Retrieves the primary text generation model file (e.g., Qwen3VL-32B-Instruct-Q4_K_M.gguf).
        /// Explicitly excludes any multimodal projector files.
        /// </summary>
        public static ModelFile? GetMainTextFile(this ModelManifest manifest)
        {
            if (manifest?.Files == null) return null;

            return manifest.Files.FirstOrDefault(f =>
                f.FileName.EndsWith(".gguf", System.StringComparison.OrdinalIgnoreCase) &&
                !f.FileName.Contains("mmproj", System.StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Retrieves the multimodal vision projector file (e.g., mmproj-Qwen3VL-32B-Instruct-Q8_0.gguf) if present.
        /// </summary>
        public static ModelFile? GetVisionProjectorFile(this ModelManifest manifest)
        {
            if (manifest?.Files == null) return null;

            return manifest.Files.FirstOrDefault(f =>
                f.FileName.Contains("mmproj", System.StringComparison.OrdinalIgnoreCase));
        }
    }
}