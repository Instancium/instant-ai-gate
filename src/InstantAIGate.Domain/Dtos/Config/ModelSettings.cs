using InstantAIGate.Domain.Enums;

namespace InstantAIGate.Domain.Dtos.Config
{
    /// <summary>
    /// Configuration payload containing runtime initialization parameters and hardware boundaries for a model.
    /// Maps a logical repository identifier to its physical file location on disk.
    /// </summary>
    public class ModelSettings
    {
        /// <summary>
        /// The logical repository identifier (e.g., "Qwen/Qwen2.5-7B"). Used as the unique key across API boundaries.
        /// </summary>
        public string RepoId { get; set; } = string.Empty;

        /// <summary>
        /// The absolute physical storage path to the GGUF binary file required by the native LLama runtime.
        /// </summary>
        public string ModelPath { get; set; } = string.Empty;

        /// <summary>
        /// Absolute path to the multimodal projector file (.mmproj).
        /// </summary>
        public string? ProjectorPath { get; set; }

        /// <summary>
        /// Indicates whether the model configuration includes multimodal support.
        /// </summary>
        public bool VisionSupport => !string.IsNullOrWhiteSpace(ProjectorPath);

        /// <summary>
        /// Explicit flag indicating whether the computational graph should expose text vectorization vector layers.
        /// </summary>
        public bool Embeddings { get; set; }

        /// <summary>
        /// The maximum sequence length (token window size) allocated for an individual context evaluation loop.
        /// </summary>
        public uint ContextSize { get; set; } = 2048;

        /// <summary>
        /// The number of layer matrices offloaded directly onto the GPU. Evaluates to -1 to offload all layers automatically.
        /// </summary>
        public int GpuLayerCount { get; set; } = 20;

        /// <summary>
        /// Enables the highly-optimized Flash Attention computation kernels to accelerate inference and reduce VRAM memory footprints.
        /// </summary>
        public bool FlashAttention { get; set; } = true;

        /// <summary>
        /// The number of physical CPU core computing threads assigned to execute tensor calculations.
        /// </summary>
        public int Threads { get; set; } = 4;

        /// <summary>
        /// Standard model architecture category taxonomy descriptor (e.g., LLM or Embedding).
        /// </summary>
        public ModelType Type { get; set; } = ModelType.Llm;

        /// <summary>
        /// The maximum capacity limit for the context pool, defining the concurrent multi-user capacity window.
        /// </summary>
        public int MaxContexts { get; set; } = 2;

        /// <summary>
        /// The maximum number of tokens processed in a single batch during inference or embedding generation. 
        /// Larger values may improve throughput but require additional VRAM.
        /// </summary>
        public uint BatchSize { get; set; } = 512;

        /// <summary>
        /// Indicates whether to lock the model weights in physical RAM to prevent OS paging or swapping. 
        /// This improves inference stability and latency at the cost of higher resident memory usage.
        /// </summary>
        public bool UseMemoryLock { get; set; } = false;

        /// <summary>
        /// The index of the primary GPU device to be used for computation and memory allocation. 
        /// Defaults to 0, representing the first available GPU in the system.
        /// </summary>
        public int MainGPU { get; set; } = 0;

        /// <summary>
        /// The quantization format for the Key-Value (KV) cache. 
        /// Reducing precision (e.g., to "Q8_K" or "Q4_K") decreases VRAM consumption during long context windows, 
        /// with a potential minor trade-off in accuracy. Valid values include "F16", "Q8_K", "Q5_K", and "Q4_K".
        /// </summary>
        public string KvCacheQuantization { get; set; } = "F16";

        /// <summary>
        /// Traditional parameter-free constructor for standard serialization engines.
        /// </summary>
        public ModelSettings() { }

        /// <summary>
        /// Convenience constructor to initialize structural mapping presets.
        /// </summary>
        public ModelSettings(string repoId, string modelPath)
        {
            RepoId = repoId;
            ModelPath = modelPath;
        }

        /// <summary>
        /// Maximum allowed model file size in megabytes (MB). Checked when loading GGUF/weights from disk.
        /// Specify an approximate size of the weight file in megabytes. Defaults to 32768 MB (32 GB).
        /// </summary>
        public int MaxModelFileSizeMb { get; set; } = 32768;
    }
}