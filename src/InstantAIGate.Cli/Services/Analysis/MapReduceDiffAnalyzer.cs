namespace InstantAIGate.Cli.Services.Analysis;

using InstantAIGate.Cli.Core;
using InstantAIGate.Core.Dtos.Inference;
using Spectre.Console;
using System;
using System.Collections.Generic;
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
        string prompt = $@"Analyze the following context (which may be raw code diffs or aggregated summaries) and generate a single commit message.
            RULES:
            1. MUST be exclusively in English.
            2. MUST use Conventional Commits format (type(scope): description).
            3. Keep under 72 characters.
            4. Output ONLY the commit message.

            Context:
            {aggregatedContext}";

        var messages = new List<ChatMessage> { new ChatMessage("user", prompt) };
        var sb = new StringBuilder();

        await foreach (var chunk in _gatewayClient.StreamChatAsync(_modelId, messages, ct))
        {
            sb.Append(chunk);
        }
        return sb.ToString().Trim(' ', '\n', '\r', '"', '\'');
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