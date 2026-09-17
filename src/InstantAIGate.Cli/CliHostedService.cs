using InstantAIGate.Cli.Commands;
using InstantAIGate.Cli.State;
using InstantAIGate.Core.Dtos.Config;
using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Interfaces.Inference;
using Microsoft.Extensions.Hosting;
using Spectre.Console;
using System.Text;

namespace InstantAIGate.Cli;

public class CliHostedService : IHostedService
{
    private readonly IHostApplicationLifetime _appLifetime;
    private readonly CommandDispatcher _commandDispatcher;
    private readonly CliSession _session;
    private readonly IInferenceEngine _inferenceEngine;
    private Task? _applicationTask;
    private CancellationTokenSource? _cancellationTokenSource;

    public CliHostedService(
        IHostApplicationLifetime appLifetime,
        CommandDispatcher commandDispatcher,
        CliSession session,
        IInferenceEngine inferenceEngine)
    {
        _appLifetime = appLifetime;
        _commandDispatcher = commandDispatcher;
        _session = session;
        _inferenceEngine = inferenceEngine;
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
                AnsiConsole.Markup("[cyan]  User:[/] ");
                var input = Console.ReadLine();

                if (string.IsNullOrWhiteSpace(input)) continue;

                var isCommand = await _commandDispatcher.TryExecuteAsync(input, cancellationToken);
                if (isCommand) continue;

                await HandleChatInferenceAsync(input, cancellationToken);
            }
        }
        finally
        {
            _appLifetime.StopApplication();
        }
    }

    private async Task HandleChatInferenceAsync(string input, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_session.ActiveModelId) || _session.ActiveModelConfig == null)
        {
            AnsiConsole.MarkupLine("[red]No model is currently loaded. Use /load <id> first.[/]");
            return;
        }

        // 1. Prepare messages
        var userMessage = new ChatMessage("user", input);
        _session.ChatHistory.Add(userMessage);

        var requestMessages = _session.ChatHistory.ToList();
        var imagesToProcess = _session.PendingImagePaths.Any() ? _session.PendingImagePaths.ToArray() : null;

        AnsiConsole.Markup("[blue] AI:[/] ");

        try
        {
            // 2. Format prompt natively via llama.cpp chat templates
            var formattedPrompt = await _inferenceEngine.ApplyChatTemplateAsync(
                _session.ActiveModelConfig.RepoId,
                requestMessages,
                imagesToProcess,
                cancellationToken);

            // 3. Configure inference settings
            var settings = new InferenceSettings
            {
                MaxTokens = 4096,
                Temperature = 0.7f,
                TopP = 0.9f,
                TopK = 40
            };

            // 4. Stream generation directly from the native wrapper
            var fullResponse = new StringBuilder();

            await foreach (var chunk in _inferenceEngine.StreamGenerationAsync(
                _session.ActiveModelConfig.RepoId,
                formattedPrompt,
                imagesToProcess,
                settings,
                cancellationToken))
            {
                AnsiConsole.Write(chunk);
                fullResponse.Append(chunk);
            }

            AnsiConsole.WriteLine();

            // 5. Update history and clear pending multimodal data
            _session.ChatHistory.Add(new ChatMessage("assistant", fullResponse.ToString()));
            _session.PendingImagePaths.Clear();
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"\n[red]Fatal Inference Error:[/] {ex.Message}");
            _session.ChatHistory.RemoveAt(_session.ChatHistory.Count - 1); // Remove failed user prompt
        }
    }

    private void RenderHeader()
    {
        AnsiConsole.Clear();
        AnsiConsole.Write(
            new FigletText("InstantAIGate")
                .LeftJustified()
                .Color(Color.Blue));

        AnsiConsole.MarkupLine("[dim]Local Inference Engine CLI - Instancium R&D (Vulkan Native)[/]");
        AnsiConsole.MarkupLine("Type [yellow]/help[/] to view available commands.\n");
    }
}