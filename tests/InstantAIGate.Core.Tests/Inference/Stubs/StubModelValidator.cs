namespace InstantAIGate.Core.Tests.Inference.Stubs;

using InstantAIGate.Core.Interfaces.Inference;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public class StubModelValidator : IModelValidator
{
    public bool ShouldPass { get; set; } = true;

    public Task<bool> ValidateIntegrityAsync(IEnumerable<string> filePaths, CancellationToken ct = default)
    {
        return Task.FromResult(ShouldPass);
    }
}