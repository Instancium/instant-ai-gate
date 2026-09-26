namespace InstantAIGate.Cli.Services.Analysis;

using InstantAIGate.Cli.Core;
using InstantAIGate.Core.Dtos.Inference;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.IO;
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

   
        var changedFiles = ExtractChangedFiles(rawDiff);

        if (rawDiff.Length <= SafeCharLimit)
        {
            return await GenerateFinalCommitAsync(rawDiff, changedFiles, ct);
        }

        AnsiConsole.MarkupLine($"\n[dim]Diff is too large ({rawDiff.Length} chars). Engaging Map-Reduce processing...[/]");

        var chunks = ChunkDiffByFiles(rawDiff, SafeCharLimit);
        var partialSummaries = new StringBuilder();

        for (int i = 0; i < chunks.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            AnsiConsole.MarkupLine($"[dim]Analyzing part {i + 1}/{chunks.Count}...[/]");
            string summary = await SummarizeChunkAsync(chunks[i], changedFiles, ct);
            partialSummaries.AppendLine($"- {summary}");
        }

        AnsiConsole.MarkupLine("[dim]Aggregating partial summaries into final commit message...[/]");
        return await GenerateFinalCommitAsync(partialSummaries.ToString(), changedFiles, ct);
    }

    private List<string> ExtractChangedFiles(string rawDiff)
    {
        var files = new List<string>();
        using var reader = new StringReader(rawDiff);
        string? line;

        while ((line = reader.ReadLine()) != null)
        {
            if (line.StartsWith("diff --git a/"))
            {
                var parts = line.Split(' ');
      
                if (parts.Length >= 3 && parts[2].StartsWith("a/"))
                {
                    files.Add(parts[2].Substring(2));
                }
            }
        }

        return files.Distinct().ToList();
    }

    private List<string> ChunkDiffByFiles(string rawDiff, int maxLength)
    {
        var chunks = new List<string>();
        var currentChunk = new StringBuilder();
        using var reader = new StringReader(rawDiff);
        string? line;

        while ((line = reader.ReadLine()) != null)
        {
            if (line.StartsWith("diff --git a/") && currentChunk.Length > 0)
            {
                if (currentChunk.Length + line.Length > maxLength)
                {
                    chunks.Add(currentChunk.ToString());
                    currentChunk.Clear();
                }
            }

            currentChunk.AppendLine(line);
        }

        if (currentChunk.Length > 0)
        {
            chunks.Add(currentChunk.ToString());
        }

        return chunks;
    }

    private async Task<string> SummarizeChunkAsync(string diffChunk, List<string> changedFiles, CancellationToken ct)
    {
        string filesList = string.Join(", ", changedFiles);
        string prompt = $"You are analyzing a partial diff for the following files: [{filesList}]. Briefly summarize the code changes in 1-2 sentences. Ignore formatting changes.\n\n{diffChunk}";

        var messages = new List<ChatMessage> { new ChatMessage("user", prompt) };
        var sb = new StringBuilder();

        await foreach (var chunk in _gatewayClient.StreamChatAsync(_modelId, messages, ct))
        {
            sb.Append(chunk);
        }

        return sb.ToString().Trim();
    }

    private async Task<string> GenerateFinalCommitAsync(string aggregatedContext, List<string> changedFiles, CancellationToken ct)
    {
        string filesList = string.Join(", ", changedFiles);
        string prompt = $@"Analyze the following diff context and generate a complete Conventional Commit message. RULES:
1. MUST be exclusively in English.
2. First line (Title): Format as type(scope): description. STRICTLY under 72 characters.
3. CRITICAL FILE TYPE RULE: You MUST base the 'type' and 'scope' on the actual files changed: [{filesList}]. 
   - If ONLY documentation files (.md, .txt) are changed, type MUST be 'docs' regardless of the technical terms in the text.
   - Do not guess the architectural scope based on the text if the file name dictates otherwise.
4. Second line: MUST be completely blank.
5. Third line onwards (Body): Provide a concise bulleted list detailing WHAT was changed.

Context: {aggregatedContext}";

        var messages = new List<ChatMessage> { new ChatMessage("user", prompt) };
        var sb = new StringBuilder();

        await foreach (var chunk in _gatewayClient.StreamChatAsync(_modelId, messages, ct))
        {
            sb.Append(chunk);
        }

        return sb.ToString().Trim();
    }
}