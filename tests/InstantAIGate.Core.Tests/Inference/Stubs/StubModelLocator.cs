namespace InstantAIGate.Core.Tests.Inference.Stubs;

using InstantAIGate.Core.Dtos.Config;
using InstantAIGate.Core.Interfaces.Inference;
using System.Threading;
using System.Threading.Tasks;

public class StubModelLocator : IModelLocator
{
    private readonly string _modelPath;
    private readonly string? _projectorPath;

    public StubModelLocator(string modelPath, string? projectorPath = null)
    {
        _modelPath = modelPath;
        _projectorPath = projectorPath;
    }

    public Task<ResolvedModelPaths> ResolvePathsAsync(string repoId, bool visionSupport, CancellationToken ct = default)
    {
        // For testing, we bypass automatic search and return the exact hardcoded paths
        // We only return the projector path if visionSupport is explicitly requested
        return Task.FromResult(new ResolvedModelPaths(
            PrimaryModelPath: _modelPath,
            VisionProjectorPath: visionSupport ? _projectorPath : null,
            TotalSizeBytes: 5000000000
        ));
    }
}