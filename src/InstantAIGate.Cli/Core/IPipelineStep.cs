namespace InstantAIGate.Cli.Core;

using InstantAIGate.Cli.State;
using System.Threading;
using System.Threading.Tasks;

public interface IPipelineStep
{
    string Name { get; }
    Task ExecuteAsync(PipelineContext context, CancellationToken cancellationToken);
}