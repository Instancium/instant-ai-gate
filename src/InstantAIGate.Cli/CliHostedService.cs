namespace InstantAIGate.Cli;

using InstantAIGate.Cli.Commands;
using InstantAIGate.Cli.Core;
using InstantAIGate.Cli.Services;
using InstantAIGate.Cli.State;
using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Dtos.Status;
using InstantAIGate.Core.Exceptions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

public class CliHostedService : IHostedService
{
    private readonly IHostApplicationLifetime _appLifetime;
    private readonly CommandDispatcher _commandDispatcher;
    private readonly CliSession _session;
    private readonly IGatewayClient _gatewayClient;
    private readonly ClientSessionMemoryCoordinator _memoryCoordinator;
    private Task? _applicationTask;
    private CancellationTokenSource? _cancellationTokenSource;
    private volatile string? _activeQueueStatus;
    private readonly ILogger<CliHostedService> _logger;

    public CliHostedService(
        IHostApplicationLifetime appLifetime,
        CommandDispatcher commandDispatcher,
        CliSession session,
        IGatewayClient gatewayClient,
        ClientSessionMemoryCoordinator memoryCoordinator,
        ILogger<CliHostedService> logger)
    {
        _appLifetime = appLifetime;
        _commandDispatcher = commandDispatcher;
        _session = session;
        _gatewayClient = gatewayClient;
        _memoryCoordinator = memoryCoordinator;
        _logger = logger;

        _gatewayClient.QueuePositionReceived += OnQueuePositionReceived;
        _gatewayClient.LogReceived += OnLogReceived;
    }

    private void OnQueuePositionReceived(int position)
    {
        if (position > 0)
        {
            _activeQueueStatus = $"[yellow]Position in inference queue: #{position}...[/]";
            AnsiConsole.MarkupLine($"\r{_activeQueueStatus}");
        }
        else
        {
            _activeQueueStatus = null;
        }
    }

    private void OnLogReceived(string level, string category, string message)
    {
        _logger.LogInformation("[{Level}] {Category}: {Message}", level, category, message);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _applicationTask = Task.Run(() => RunInteractiveLoopAsync(_cancellationTokenSource.Token), cancellationToken);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _gatewayClient.QueuePositionReceived -= OnQueuePositionReceived;
        _gatewayClient.LogReceived -= OnLogReceived;

        if (_cancellationTokenSource != null)
        {
            await _cancellationTokenSource.CancelAsync();
        }

        if (_applicationTask != null)
        {
            await Task.WhenAny(_applicationTask, Task.Delay(Timeout.Infinite, cancellationToken));
        }
    }

    private async Task RunInteractiveLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            RenderHeader();

            if (_session.IsRemoteMode)
            {
                await CheckInitialRemoteStatusAsync(cancellationToken);
            }

            while (!cancellationToken.IsCancellationRequested && !_session.IsExitRequested)
            {
                try
                {
                    AnsiConsole.Markup("\n[bold cyan]👤 User:[/] ");
                    var input = Console.ReadLine();

                    if (string.IsNullOrWhiteSpace(input))
                    {
                        continue;
                    }

                    var isCommand = await _commandDispatcher.TryExecuteAsync(input, cancellationToken);
                    if (isCommand)
                    {
                        continue;
                    }

                    await HandleChatInferenceAsync(input, cancellationToken);
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"\n[red]CLI Error:[/] {Markup.Escape(ex.Message)}");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _appLifetime.StopApplication();
        }
    }

    private async Task CheckInitialRemoteStatusAsync(CancellationToken ct)
    {
        try
        {
            var status = await _gatewayClient.GetGatewayStatusAsync(ct);
            if (status.Status == GatewayOperationalStatus.ModelDownloading)
            {
                AnsiConsole.MarkupLine($"[yellow]Notice:[/] Remote Gateway is currently downloading startup model [bold cyan]{Markup.Escape(status.ActiveModelId ?? string.Empty)}[/] ({status.ProgressPercentage:F1}%).");
                AnsiConsole.MarkupLine("[dim]Type [cyan]/connect[/] to view real-time download progress or wait for completion.[/]\n");
            }
            else if (status.Status == GatewayOperationalStatus.ModelLoading)
            {
                AnsiConsole.MarkupLine($"[yellow]Notice:[/] Remote Gateway is loading model [bold cyan]{Markup.Escape(status.ActiveModelId ?? string.Empty)}[/] into memory.\n");
            }
            else if (status.Status == GatewayOperationalStatus.Ready)
            {
                _session.ActiveModelId = status.ActiveModelId;
                AnsiConsole.MarkupLine($"[green]Connected to Remote Gateway. Active model:[/] [bold cyan]{Markup.Escape(status.ActiveModelId ?? string.Empty)}[/] [dim](Ready)[/]\n");
            }
        }
        catch (Exception ex)
        {
            _logger.LogTrace(ex, "Could not query initial remote gateway status.");
        }
    }

    private async Task HandleChatInferenceAsync(string input, CancellationToken cancellationToken)
    {
        if (!_session.IsRemoteMode && string.IsNullOrEmpty(_session.ActiveModelId))
        {
            AnsiConsole.MarkupLine("[red]No model is currently loaded. Use /load <id> first.[/]");
            return;
        }

        var parts = new List<MessageContent>();
        if (_session.PendingMedia.Count > 0)
        {
            parts.AddRange(_session.PendingMedia);
        }
        parts.Add(new TextContent(input));

        var userMessage = new ChatMessage("user", parts);
        _session.ChatHistory.Add(userMessage);

        AnsiConsole.WriteLine();

        try
        {
            var fullResponse = new StringBuilder();
            string modelToRequest = _session.ActiveModelId ?? string.Empty;

            await foreach (var chunk in _gatewayClient.StreamChatAsync(
                _session.SessionId,
                modelToRequest,
                userMessage,
                cancellationToken))
            {
                AnsiConsole.Markup($"[silver]{Markup.Escape(chunk)}[/]");
                fullResponse.Append(chunk);
            }

            AnsiConsole.WriteLine();
            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Rule().RuleStyle("grey").LeftJustified());

            _session.ChatHistory.Add(new ChatMessage("assistant", fullResponse.ToString()));
            _session.PendingMedia.Clear();
        }
        catch (ContextOverflowException ex)
        {
            CliMarkup.Line($"\n[bold yellow]Context Window Boundary Reached![/] [[{ex.PastTokens} + {ex.IncomingTokens} + {ex.ReservedTokens} > {ex.ContextSize} tokens]]");
            await HandleContextOverflowMitigationAsync(ex, cancellationToken);
        }
        catch (Exception ex) when (ex.Message.Contains("was not found", StringComparison.OrdinalIgnoreCase))
        {
            AnsiConsole.MarkupLine("\n[bold red]Server State Lost:[/] The server was restarted or lost the session. Physical KV-cache destroyed.");
            AnsiConsole.MarkupLine("[yellow]Local history has been cleared. Please start a new dialogue.[/]");

            if (_session.ChatHistory.Count > 0)
            {
                _session.ChatHistory.RemoveAt(_session.ChatHistory.Count - 1);
            }
            _session.ClearHistory();
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"\n[red]Fatal Inference Error:[/] {Markup.Escape(ex.Message)}");
            if (_session.ChatHistory.Count > 0)
            {
                _session.ChatHistory.RemoveAt(_session.ChatHistory.Count - 1);
            }
        }
    }

    private async Task HandleContextOverflowMitigationAsync(ContextOverflowException ex, CancellationToken cancellationToken)
    {
        const string optionShift = "1. Truncate context via sliding window (ShiftSessionCache)";
        const string optionRollback = "2. Rollback context to previous turn (RollbackSession)";
        const string optionReset = "3. Clear conversation context and start fresh";
        const string optionCancel = "4. Cancel request";

        var action = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[cyan]Select a context recovery action:[/]")
                .AddChoices(optionShift, optionRollback, optionReset, optionCancel));

        switch (action)
        {
            case optionShift:
                int overflowExcess = (ex.PastTokens + ex.IncomingTokens + ex.ReservedTokens) - ex.ContextSize;
                int shiftAmount = Math.Max(overflowExcess + 128, 256);

                bool shifted = await _memoryCoordinator.TruncateSlidingWindowAsync(
                    _session.SessionId,
                    systemPrefixTokens: 0,
                    tokensToEvict: shiftAmount,
                    cancellationToken);

                if (shifted)
                {
                    AnsiConsole.MarkupLine($"[green]Sliding window applied: evicted {shiftAmount} tokens. You can retry your prompt.[/]");
                }
                else
                {
                    AnsiConsole.MarkupLine("[yellow]Sliding window shift was not possible. Rolled back to safe state.[/]");
                }
                break;

            case optionRollback:
                int targetRollback = Math.Max(0, ex.PastTokens - 256);
                await _memoryCoordinator.RollbackToCheckpointAsync(_session.SessionId, targetRollback, cancellationToken);
                if (_session.ChatHistory.Count > 0)
                {
                    _session.ChatHistory.RemoveAt(_session.ChatHistory.Count - 1);
                }
                AnsiConsole.MarkupLine($"[green]Context rolled back to token position {targetRollback}.[/]");
                break;

            case optionReset:
                await _memoryCoordinator.EvictSessionAsync(_session.SessionId, cancellationToken);
                _session.ClearHistory();
                AnsiConsole.MarkupLine("[green]Session context purged. Ready for fresh conversation.[/]");
                break;

            case optionCancel:
            default:
                if (_session.ChatHistory.Count > 0)
                {
                    _session.ChatHistory.RemoveAt(_session.ChatHistory.Count - 1);
                }
                AnsiConsole.MarkupLine("[dim]Prompt discarded.[/]");
                break;
        }
    }

    private void RenderHeader()
    {
        AnsiConsole.Clear();
        AnsiConsole.Write(
            new FigletText("InstantAIGate")
                .LeftJustified()
                .Color(Color.Blue));
        AnsiConsole.MarkupLine("[dim]High-Performance On-Premises Inference Runtime[/]");
        AnsiConsole.MarkupLine("Type [yellow]/help[/] to view available commands.\n");
    }
}