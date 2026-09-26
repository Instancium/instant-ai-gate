// File: src\InstantAIGate.Cli\Pipeline\Steps\GenerateCommitMessageStep.cs
namespace InstantAIGate.Cli.Pipeline.Steps;

using InstantAIGate.Cli.Configuration;
using InstantAIGate.Cli.Core;
using InstantAIGate.Cli.Services;
using InstantAIGate.Cli.State;
using InstantAIGate.Core.Dtos.Inference;
using Microsoft.Extensions.Options;
using Spectre.Console;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

public class GenerateCommitMessageStep : IPipelineStep
{
    private readonly IGitService _gitService;
    private readonly IGatewayClient _gatewayClient;
    private readonly ReleasePipelineSettings _settings;

    public string Name => "Generate Commit Message via AI";

    public GenerateCommitMessageStep(IGitService gitService, IGatewayClient gatewayClient, IOptions<ReleasePipelineSettings> settings)
    {
        _gitService = gitService;
        _gatewayClient = gatewayClient;
        _settings = settings.Value;
    }

    public async Task ExecuteAsync(PipelineContext context, CancellationToken cancellationToken)
    {
        string diff = await _gitService.GetCachedDiffAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(diff))
        {
            context.CommitMessage = "chore: auto-commit format updates";
            return;
        }

        int charCount = diff.Length;
        int byteCount = Encoding.UTF8.GetByteCount(diff);

        AnsiConsole.MarkupLine($"\n[dim]Captured diff size: {charCount} characters ({byteCount / 1024.0:F2} KB)[/]");

        const int maxDiffCharacters = 3500;
        if (charCount > maxDiffCharacters)
        {
            AnsiConsole.MarkupLine($"[yellow]Warning: Diff exceeds {maxDiffCharacters} characters. Truncating to protect KV Cache...[/]");
            diff = diff.Substring(0, maxDiffCharacters) + "\n...[TRUNCATED FOR CONTEXT SIZE LIMIT]";
        }

        string prompt = $@"Analyze the following git diff and generate a single commit message.
            RULES:
            1. MUST be exclusively in English.
            2. MUST use Conventional Commits format (type(scope): description).
            3. Imperative mood (e.g., 'add feature' not 'added feature').
            4. Keep under 72 characters.
            5. Provide ONLY the commit message, no markdown, no quotes, no extra text.

            Diff:
            {diff}";

        var messages = new List<ChatMessage> { new ChatMessage("user", prompt) };
        var sb = new StringBuilder();

        await _gatewayClient.LoadModelAsync(_settings.AiModelId, cancellationToken);
        await foreach (var chunk in _gatewayClient.StreamChatAsync(_settings.AiModelId, messages, cancellationToken))
        {
            sb.Append(chunk);
        }

        context.CommitMessage = sb.ToString().Trim(' ', '\n', '\r', '"', '\'');

        AnsiConsole.MarkupLine($"[green]Generated Message:[/] {context.CommitMessage}");
    }
}