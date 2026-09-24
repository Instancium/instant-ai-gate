using InstantAIGate.Cli.Commands;
using InstantAIGate.Cli.Core;
using InstantAIGate.Cli.State;
using InstantAIGate.Core.Dtos.Inference;
using Microsoft.Extensions.Hosting;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace InstantAIGate.Cli;

public class CliHostedService : IHostedService
{
    private readonly IHostApplicationLifetime _appLifetime;
    private readonly CommandDispatcher _commandDispatcher;
    private readonly CliSession _session;
    private readonly IGatewayClient _gatewayClient;
    private Task? _applicationTask;
    private CancellationTokenSource? _cancellationTokenSource;

    public CliHostedService(
        IHostApplicationLifetime appLifetime,
        CommandDispatcher commandDispatcher,
        CliSession session,
        IGatewayClient gatewayClient)
    {
        _appLifetime = appLifetime;
        _commandDispatcher = commandDispatcher;
        _session = session;
        _gatewayClient = gatewayClient;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _applicationTask = Task.Run(() => RunInteractiveLoopAsync(_cancellationTokenSource.Token), cancellationToken);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
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
            while (!cancellationToken.IsCancellationRequested && !_session.IsExitRequested)
            {
                try
                {
                    AnsiConsole.Markup("[cyan]  User:[/] ");
                    var input = Console.ReadLine();
                    if (string.IsNullOrWhiteSpace(input)) continue;

                    var isCommand = await _commandDispatcher.TryExecuteAsync(input, cancellationToken);
                    if (isCommand) continue;

                    await HandleChatInferenceAsync(input, cancellationToken);
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"\n[red]CLI Error:[/] {ex.Message}");
                }
            }
        }
        finally
        {
            _appLifetime.StopApplication();
        }
    }

    private async Task HandleChatInferenceAsync(string input, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_session.ActiveModelId))
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

        AnsiConsole.Markup("[blue] AI:[/] ");

        try
        {
            var fullResponse = new StringBuilder();

            await foreach (var chunk in _gatewayClient.StreamChatAsync(
                _session.ActiveModelId,
                _session.ChatHistory,
                cancellationToken))
            {
                AnsiConsole.Write(chunk);
                fullResponse.Append(chunk);
            }

            AnsiConsole.WriteLine();
            _session.ChatHistory.Add(new ChatMessage("assistant", fullResponse.ToString()));
            _session.PendingMedia.Clear();
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"\n[red]Fatal Inference Error:[/] {ex.Message}");
            _session.ChatHistory.RemoveAt(_session.ChatHistory.Count - 1);
        }
    }

    private void RenderHeader()
    {
        AnsiConsole.Clear();
        AnsiConsole.Write(
            new FigletText("InstantAIGate")
                .LeftJustified()
                .Color(Color.Blue));

        AnsiConsole.MarkupLine("[dim]Gateway CLI - Local & Remote Access Ready[/]");
        AnsiConsole.MarkupLine("Type [yellow]/help[/] to view available commands.\n");
    }
}