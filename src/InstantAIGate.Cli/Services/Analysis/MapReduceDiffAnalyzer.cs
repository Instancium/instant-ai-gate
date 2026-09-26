namespace InstantAIGate.Cli.Services.Analysis;

using InstantAIGate.Cli.Core;
using InstantAIGate.Core.Dtos.Inference;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

public class MapReduceDiffAnalyzer : IDiffAnalyzer
{
    private readonly IGatewayClient _gatewayClient;
    private readonly string _modelId;
    private const int SafeCharLimit = 3000;

    public MapReduceDiffAnalyzer(IGatewayClient gatewayClient, string modelId)
    {
        _gatewayClient = gatewayClient;
        _modelId = modelId;
    }

    public async Task<string> AnalyzeAndSummarizeAsync(string rawDiff, CancellationToken ct)
    {
        AnsiConsole.MarkupLine($"[dim]Verifying model '{_modelId}' is loaded in VRAM...[/]");
        await _gatewayClient.LoadModelAsync(_modelId, ct);

        if (rawDiff.Length <= SafeCharLimit)
        {
            return await GenerateFinalCommitAsync(rawDiff, ct);
        }

        AnsiConsole.MarkupLine($"\n[dim]Diff is too large ({rawDiff.Length} chars). Engaging Map-Reduce processing...[/]");

        var chunks = ChunkDiffByFiles(rawDiff, SafeCharLimit);
        var partialSummaries = new StringBuilder();

        for (int i = 0; i < chunks.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            AnsiConsole.MarkupLine($"[dim]Analyzing part {i + 1}/{chunks.Count}...[/]");

            string summary = await SummarizeChunkAsync(chunks[i], ct);
            partialSummaries.AppendLine($"- {summary}");
        }

        AnsiConsole.MarkupLine("[dim]Aggregating partial summaries into final commit message...[/]");
        return await GenerateFinalCommitAsync(partialSummaries.ToString(), ct);
    }

    private async Task<string> SummarizeChunkAsync(string diffChunk, CancellationToken ct)
    {
        string prompt = $"Briefly summarize the following code changes in 1-2 sentences. Ignore formatting changes.\n\n{diffChunk}";
        var messages = new List<ChatMessage> { new ChatMessage("user", prompt) };

        var sb = new StringBuilder();
        await foreach (var chunk in _gatewayClient.StreamChatAsync(_modelId, messages, ct))
        {
            sb.Append(chunk);
        }
        return sb.ToString().Trim();
    }

    private async Task<string> GenerateFinalCommitAsync(string aggregatedContext, CancellationToken ct)
    {
        string prompt = $@"Analyze the following context and generate a complete Conventional Commit message.
RULES:
1. MUST be exclusively in English.
2. First line (Title): Format as `type(scope): description`. Imperative mood. STRICTLY under 72 characters.
3. Second line: MUST be completely blank.
4. Third line onwards (Body): Provide a concise bulleted list detailing WHAT was changed and WHY.
5. Provide ONLY the raw commit message. Do NOT use markdown code blocks (```) or quotes.

Context:
{aggregatedContext}";

        var messages = new List<ChatMessage> { new ChatMessage("user", prompt) };
        var sb = new StringBuilder();

        await foreach (var chunk in _gatewayClient.StreamChatAsync(_modelId, messages, ct))
        {
            sb.Append(chunk);
        }

        string rawMessage = sb.ToString().Trim(' ', '\n', '\r', '`', '"', '\'');
        return EnforceCommitFormat(rawMessage);
    }

    private string EnforceCommitFormat(string rawMessage)
    {
        var lines = rawMessage.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).ToList();

        if (lines.Count == 0) return "chore: auto-commit updates";

        // 1. Programmatically enforce the 72-character limit on the title line
        if (lines[0].Length > 72)
        {
            lines[0] = lines[0].Substring(0, 69) + "...";
        }

        // 2. Programmatically enforce the blank second line for Conventional Commits
        if (lines.Count > 1 && !string.IsNullOrWhiteSpace(lines[1]))
        {
            lines.Insert(1, string.Empty);
        }

        return string.Join(Environment.NewLine, lines);
    }

    private List<string> ChunkDiffByFiles(string diff, int maxCharsPerChunk)
    {
        var fileBlocks = diff.Split(new[] { "diff --git a/" }, StringSplitOptions.RemoveEmptyEntries);
        var chunks = new List<string>();
        var currentChunk = new StringBuilder();

        foreach (var block in fileBlocks)
        {
            string formattedBlock = "diff --git a/" + block;

            if (currentChunk.Length + formattedBlock.Length > maxCharsPerChunk && currentChunk.Length > 0)
            {
                chunks.Add(currentChunk.ToString());
                currentChunk.Clear();
            }

            currentChunk.Append(formattedBlock);
        }

        if (currentChunk.Length > 0)
        {
            chunks.Add(currentChunk.ToString());
        }

        return chunks;
    }
}