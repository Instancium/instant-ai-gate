namespace InstantAIGate.Core.Dtos.Config
{
    public record HardwareProfileSettings
    {
        public int GpuLayerCount { get; init; } = 99;
        public int MainGPU { get; init; } = 0;
        public int ContextSize { get; init; } = 2048;
        public int BatchSize { get; init; } = 512;
        public int Threads { get; init; } = 4;
        public bool FlashAttention { get; init; } = false;
        public bool Embeddings { get; init; } = false;
        public string KvCacheQuantization { get; init; } = "F16";
        public bool UseMemoryLock { get; init; } = false;
        public int MaxContexts { get; init; } = 4;
    }
}
