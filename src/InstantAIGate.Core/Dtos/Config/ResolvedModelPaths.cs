// File: src/InstantAIGate.Core/Dtos/Config/ResolvedModelPaths.cs
namespace InstantAIGate.Core.Dtos.Config;

public record ResolvedModelPaths(
    string PrimaryModelPath,
    string? VisionProjectorPath,
    long TotalSizeBytes
);