namespace InstantAIGate.Cli.Commands;

using InstantAIGate.Cli.State;
using InstantAIGate.Core.Dtos.Inference;
using Spectre.Console;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

public class ImageCommand : IConsoleCommand
{
    private readonly CliSession _session;

    public ImageCommand(CliSession session)
    {
        _session = session;
    }

    public string Name => "/image";
    public string Description => "Attaches a local image to the next prompt (e.g., /image C:\\models\\test.jpg).";

    public Task ExecuteAsync(string argument, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(argument))
        {
            AnsiConsole.MarkupLine("[red]Usage: /image <file_path>[/]");
            return Task.CompletedTask;
        }

        var cleanPath = argument.Trim('"', '\'', ' ');
        if (!File.Exists(cleanPath))
        {
            AnsiConsole.MarkupLine($"[red]Error: File not found at '{cleanPath}'[/]");
            return Task.CompletedTask;
        }

        _session.PendingMedia.Add(new ImageFileContent(cleanPath));
        AnsiConsole.MarkupLine($"[green]Image queued for next request:[/] {cleanPath}");

        return Task.CompletedTask;
    }
}