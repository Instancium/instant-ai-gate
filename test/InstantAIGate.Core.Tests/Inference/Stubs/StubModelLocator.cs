// File: test/InstantAIGate.Core.Tests/Inference/Stubs/StubModelLocator.cs
namespace InstantAIGate.Core.Tests.Inference.Stubs;

using InstantAIGate.Core.Dtos.Config;
using InstantAIGate.Core.Interfaces.Inference;
using System.Threading;
using System.Threading.Tasks;

public class StubModelLocator : IModelLocator
{
    private readonly string _modelPath;
    private readonly string _projectorPath;

    public StubModelLocator(string modelPath, string projectorPath)
    {
        _modelPath = modelPath;
        _projectorPath = projectorPath;
    }

    public Task<ResolvedModelPaths> ResolvePathsAsync(ModelSettings config, CancellationToken ct = default)
    {
        // For testing, we bypass automatic search and return the exact hardcoded paths
        return Task.FromResult(new ResolvedModelPaths(_modelPath, _projectorPath, 5000000000));
    }
}