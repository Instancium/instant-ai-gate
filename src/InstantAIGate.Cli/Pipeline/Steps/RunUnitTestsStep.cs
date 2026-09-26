namespace InstantAIGate.Cli.Pipeline.Steps;

using InstantAIGate.Cli.Core;
using InstantAIGate.Cli.Services;
using InstantAIGate.Cli.State;
using System.Threading;
using System.Threading.Tasks;

public class RunUnitTestsStep : IPipelineStep
{
    private readonly IDotnetService _dotnetService;
    public string Name => "Run Unit Tests (Quality Gate)";

    public RunUnitTestsStep(IDotnetService dotnetService)
    {
        _dotnetService = dotnetService;
    }

    public async Task ExecuteAsync(PipelineContext context, CancellationToken cancellationToken)
    {
        await _dotnetService.RunUnitTestsAsync(cancellationToken);
    }
}