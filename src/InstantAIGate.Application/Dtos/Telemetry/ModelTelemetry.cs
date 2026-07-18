namespace InstantAIGate.Application.Dtos.Telemetry
{

    /// <summary>
    /// Deterministic operational metrics for a specific loaded model.
    /// </summary>
    public class ModelTelemetry
    {
        public string RepoId { get; set; } = string.Empty;
        public bool IsLoaded { get; set; }

        // Resource Allocation
        public uint ContextSize { get; set; }
        public int GpuLayers { get; set; }
        public int Threads { get; set; }
        public bool FlashAttention { get; set; }

        // Concurrency and Pool Status
        public int IdleContextsCount { get; set; }
        public int ActiveUsersCount { get; set; }
        public int MaxParallelUsers { get; set; }
        public bool IsQueueWaiting { get; set; }
    }
}
