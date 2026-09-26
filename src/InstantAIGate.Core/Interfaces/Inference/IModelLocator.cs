namespace InstantAIGate.Core.Interfaces.Inference;

using InstantAIGate.Core.Dtos.Config;
using System.Threading;
using System.Threading.Tasks;

public interface IModelLocator
{
    Task<ResolvedModelPaths> ResolvePathsAsync(string repoId, bool visionSupport, CancellationToken ct = default);
}