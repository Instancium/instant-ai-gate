using InstantAIGate.Domain.Enums;

namespace InstantAIGate.Application.Dtos.Inference
{

    /// <summary>
    /// Represents an operational telemetry snapshot of an active model instance.
    /// Contains exclusively low-level engine execution and allocation metrics.
    /// </summary>
    public record ModelRegistryStatus(
        string RepoId,               // Logical identifier used for control plane aggregation
        bool IsLoaded,               // Activation flag indicating the engine runtime status
        int AvailableContexts,       // Current number of vacant slots available in the context pool
        int MaxParallelUsers,        // Total capacity slots (MaxContexts) allocated at pipeline startup
        int GpuLayersAllocated,       // Actual number of structural layers offloaded onto VRAM
        ModelType Type
    );
}
