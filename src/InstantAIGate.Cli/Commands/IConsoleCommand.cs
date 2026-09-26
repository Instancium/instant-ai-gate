namespace InstantAIGate.Cli.Commands;

public interface IConsoleCommand
{
    string Name { get; }
    string Description { get; }
    Task ExecuteAsync(string argument, CancellationToken cancellationToken);
}