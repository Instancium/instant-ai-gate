namespace InstantAIGate.Application.Dtos.Inference
{
    public sealed class NativeModelDetails
    {
        public string RepoId { get; set; } = string.Empty;
        public uint ContextSize { get; set; }
        public int GpuLayers { get; set; }
        public int Threads { get; set; }
        public bool FlashAttention { get; set; }
        public int IdleContextsCount { get; set; }
        public double VramFootprintGb { get; set; }
        public string Backend { get; set; } = string.Empty;
    }
}
