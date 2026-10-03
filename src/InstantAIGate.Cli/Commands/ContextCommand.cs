namespace InstantAIGate.Cli.Commands;

using InstantAIGate.Cli.Services;
using InstantAIGate.Cli.State;
using Spectre.Console;
using System;
using System.Threading;
using System.Threading.Tasks;

public sealed class ContextCommand : IConsoleCommand
{
    private readonly ClientSessionMemoryCoordinator _memoryCoordinator;
    private readonly CliSession _session;

    public ContextCommand(ClientSessionMemoryCoordinator memoryCoordinator, CliSession session)
    {
        _memoryCoordinator = memoryCoordinator ?? throw new ArgumentNullException(nameof(memoryCoordinator));
        _session = session ?? throw new ArgumentNullException(nameof(session));
    }

    public string Name => "/ctx";
    public string Description => "Manages session context and KV-cache: /ctx info, /ctx shift <pos> <count>, /ctx rollback <pos>, /ctx clear.";

    public async Task ExecuteAsync(string argument, CancellationToken cancellationToken)
    {
        var parts = (argument ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var subCommand = parts.Length > 0 ? parts[0].ToLowerInvariant() : "info";

        switch (subCommand)
        {
            case "info":
                await ShowContextInfoAsync(cancellationToken);
                break;

            case "shift":
                await HandleShiftAsync(parts, cancellationToken);
                break;

            case "rollback":
                await HandleRollbackAsync(parts, cancellationToken);
                break;

            case "clear":
                await HandleClearAsync(cancellationToken);
                break;

            default:
                AnsiConsole.MarkupLine("[red]Unknown subcommand. Usage: /ctx [info | shift <pos> <count> | rollback <pos> | clear][/]");
                break;
        }
    }

    private async Task ShowContextInfoAsync(CancellationToken ct)
    {
        try
        {
            var metrics = await _memoryCoordinator.AuditContextAsync(_session.SessionId, systemPrefixTokens: 0, ct);

            var table = new Table().Border(TableBorder.Rounded).Expand();
            table.AddColumn("[cyan]Metric[/]");
            table.AddColumn("[cyan]Value[/]");

            table.AddRow("Session ID", Markup.Escape(metrics.SessionId));
            table.AddRow("Active Model", string.IsNullOrEmpty(metrics.RepoId) ? "[dim]none[/]" : Markup.Escape(metrics.RepoId));
            table.AddRow("Context Capacity", $"{metrics.ContextCapacity:N0} tokens");
            table.AddRow("KV-Cache Tokens", $"{metrics.PastTokensCount:N0} tokens");
            table.AddRow("Free Reserve", $"{metrics.AvailableTokensReserve:N0} tokens");

            string utilColor = metrics.UtilizationPercentage > 85 ? "red" : (metrics.UtilizationPercentage > 60 ? "yellow" : "green");
            table.AddRow("Utilization", $"[{utilColor}]{metrics.UtilizationPercentage:F2}%[/]");

            AnsiConsole.Write(table);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Failed to retrieve context info:[/] {Markup.Escape(ex.Message)}");
        }
    }

    private async Task HandleShiftAsync(string[] parts, CancellationToken ct)
    {
        if (parts.Length < 3 || !int.TryParse(parts[1], out int startPos) || !int.TryParse(parts[2], out int count))
        {
            AnsiConsole.MarkupLine("[red]Usage: /ctx shift <startPos> <count>[/]");
            return;
        }

        try
        {
            bool shifted = await _memoryCoordinator.TruncateSlidingWindowAsync(_session.SessionId, startPos, count, ct);
            if (shifted)
            {
                AnsiConsole.MarkupLine($"[green]Successfully shifted KV-cache: evicted {count} tokens starting at {startPos}.[/]");
            }
            else
            {
                AnsiConsole.MarkupLine("[yellow]Cache shift could not be performed with current parameters.[/]");
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Context shift failed:[/] {Markup.Escape(ex.Message)}");
        }
    }

    private async Task HandleRollbackAsync(string[] parts, CancellationToken ct)
    {
        if (parts.Length < 2 || !int.TryParse(parts[1], out int targetPos))
        {
            AnsiConsole.MarkupLine("[red]Usage: /ctx rollback <targetPosition>[/]");
            return;
        }

        try
        {
            await _memoryCoordinator.RollbackToCheckpointAsync(_session.SessionId, targetPos, ct);
            AnsiConsole.MarkupLine($"[green]Context rolled back to position {targetPos}.[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Rollback failed:[/] {Markup.Escape(ex.Message)}");
        }
    }

    private async Task HandleClearAsync(CancellationToken ct)
    {
        try
        {
            await _memoryCoordinator.EvictSessionAsync(_session.SessionId, ct);
            _session.ClearHistory();
            AnsiConsole.MarkupLine("[green]Session evicted and local dialogue history cleared.[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Clear failed:[/] {Markup.Escape(ex.Message)}");
        }
    }
}