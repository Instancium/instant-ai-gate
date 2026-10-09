namespace InstantAIGate.Cli.Services;

using System.Threading;
using System.Threading.Tasks;

public interface IDotnetService
{
    Task RunUnitTestsAsync(CancellationToken ct);
}
