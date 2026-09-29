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

public record FileDiffStatus(string Path, string Status);

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

        var totalFileStatuses = ExtractFileStatuses(rawDiff);

        if (rawDiff.Length <= SafeCharLimit)
        {
            return await GenerateFinalCommitAsync(rawDiff, totalFileStatuses, ct);
        }

        AnsiConsole.MarkupLine($"""

            [dim]Diff is too large ({rawDiff.Length} chars). Engaging Map-Reduce processing...[/]
            """);

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
        return await GenerateFinalCommitAsync(partialSummaries.ToString(), totalFileStatuses, ct);
    }

    private static List<FileDiffStatus> ExtractFileStatuses(string diffText)
    {
        var result = new List<FileDiffStatus>();
        using var reader = new StringReader(diffText);
        string? line;
        string? currentFile = null;
        string currentStatus = "MODIFIED";

        while ((line = reader.ReadLine()) != null)
        {
            if (line.StartsWith("diff --git a/"))
            {
                if (currentFile != null)
                {
                    result.Add(new FileDiffStatus(currentFile, currentStatus));
                }

                var parts = line.Split(' ');
                if (parts.Length >= 4 && parts[3].StartsWith("b/"))
                {
                    currentFile = parts[3][2..];
                }
                else if (parts.Length >= 3 && parts[2].StartsWith("a/"))
                {
                    currentFile = parts[2][2..];
                }
                currentStatus = "MODIFIED";
                continue;
            }

            if (line.StartsWith("new file mode"))
            {
                currentStatus = "ADDED";
            }
            else if (line.StartsWith("deleted file mode"))
            {
                currentStatus = "DELETED";
            }
            else if (line.StartsWith("similarity index") || line.StartsWith("rename from"))
            {
                currentStatus = "RENAMED";
            }
        }

        if (currentFile != null)
        {
            result.Add(new FileDiffStatus(currentFile, currentStatus));
        }

        return result.DistinctBy(f => f.Path).ToList();
    }

    private static List<string> ChunkDiffByFiles(string rawDiff, int maxLength)
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

    private async Task<string> SummarizeChunkAsync(string diffChunk, CancellationToken ct)
    {
        var chunkStatuses = ExtractFileStatuses(diffChunk);
        string filesHeader = chunkStatuses.Count > 0
            ? string.Join("\n", chunkStatuses.Select(f => $"- [{f.Status}] {f.Path}"))
            : "- [MODIFIED] Unknown files";

        string prompt = $"""
            You are an expert source code reviewer analyzing a partial git diff.

            FILES IN THIS CHUNK WITH PRECISE STATUS:
            {filesHeader}

            INSTRUCTIONS:
            1. Summarize what was modified, added, or deleted in 1-2 concise sentences.
            2. CRITICAL ACCURACY RULE:
               - If a file is marked [MODIFIED], describe updates, fixes, or extensions to existing code. NEVER state that the file is created or added, even if lines are inserted (+).
               - Only describe a file as new or created if it is explicitly marked [ADDED].
               - Describe files marked [DELETED] as removed.
            3. Do NOT output markdown code fences, headers, or quotes.

            DIFF:
            {diffChunk}
            """;

        var messages = new List<ChatMessage> { new ChatMessage("user", prompt) };
        var sb = new StringBuilder();

        await foreach (var chunk in _gatewayClient.StreamChatAsync(_modelId, messages, ct))
        {
            sb.Append(chunk);
        }

        return sb.ToString().Trim(' ', '\r', '\n', '`', '"');
    }

    private async Task<string> GenerateFinalCommitAsync(string aggregatedContext, List<FileDiffStatus> fileStatuses, CancellationToken ct)
    {
        string fileListText = string.Join("\n", fileStatuses.Select(f => $"- [{f.Status.ToUpperInvariant()}] {f.Path}"));

        string prompt = $"""
            You are an expert developer generating a Conventional Commit message.
            Strictly adhere to the provided file statuses and diff context.

            FILE CHANGE REGISTRY:
            {fileListText}

            RULES:
            1. MUST be exclusively in English.
            2. First line (Title): Format as type(scope): description. STRICTLY under 72 characters.
            3. CRITICAL STATUS ACCURACY RULES:
               - NEVER label a file as 'created', 'introduced', or 'added' if its status is [MODIFIED]. Use verbs like 'update', 'refactor', 'enhance', 'fix'.
               - ONLY treat files with status [ADDED] as newly created files.
               - Files with status [DELETED] must be described as removed or deleted.
               - If diff chunks show added lines (+) inside a [MODIFIED] file, it means code was appended or updated, NOT that the file is new.
            4. FILE TYPE AND SCOPE RULE:
               - If ONLY documentation files (.md, .txt) are changed, type MUST be 'docs'.
               - Do not guess the architectural scope from code terms if file paths indicate another layer.
            5. Second line: MUST be completely blank.
            6. Third line onwards (Body): Provide a concise bulleted list detailing WHAT was changed, preserving exact file statuses.

            Context:
            {aggregatedContext}
            """;

        var messages = new List<ChatMessage> { new ChatMessage("user", prompt) };
        var sb = new StringBuilder();

        await foreach (var chunk in _gatewayClient.StreamChatAsync(_modelId, messages, ct))
        {
            sb.Append(chunk);
        }

        return sb.ToString().Trim();
    }
}