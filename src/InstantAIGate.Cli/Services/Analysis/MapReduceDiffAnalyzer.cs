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

    private static readonly HashSet<string> IgnoredExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".lock", ".min.js", ".min.css", ".map", ".svg", ".png", ".jpg", ".jpeg",
        ".gif", ".ico", ".pdf", ".dll", ".so", ".exe", ".bin", ".gguf", ".zip",
        ".tar", ".gz", ".wasm", ".bundle.js", ".Designer.cs", ".g.cs"
    };

    private static readonly string[] IgnoredFileSubstrings =
    {
        "package-lock.json",
        "pnpm-lock.yaml",
        "yarn.lock",
        "obj/Debug/",
        "obj/Release/",
        "bin/Debug/",
        "bin/Release/",
        ".min."
    };

    public MapReduceDiffAnalyzer(IGatewayClient gatewayClient, string modelId)
    {
        _gatewayClient = gatewayClient;
        _modelId = modelId;
    }

    public Task<string> AnalyzeAndSummarizeAsync(string rawDiff, CancellationToken ct) =>
        AnalyzeAndSummarizeAsync(rawDiff, string.Empty, ct);

    public async Task<string> AnalyzeAndSummarizeAsync(string rawDiff, string diffStat, CancellationToken ct)
    {
        AnsiConsole.MarkupLine($"[dim]Verifying model '{_modelId}' is loaded in VRAM...[/]");
        await _gatewayClient.LoadModelAsync(_modelId, ct);

        var totalFileStatuses = ExtractFileStatuses(rawDiff);

        if (rawDiff.Length <= SafeCharLimit)
        {
            return await GenerateFinalCommitAsync(rawDiff, diffStat, totalFileStatuses, ct);
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
            if (!string.IsNullOrWhiteSpace(summary))
            {
                partialSummaries.AppendLine($"- {summary}");
            }
        }

        AnsiConsole.MarkupLine("[dim]Aggregating partial summaries into final commit message...[/]");
        return await GenerateFinalCommitAsync(partialSummaries.ToString(), diffStat, totalFileStatuses, ct);
    }

    private static bool IsGeneratedOrBinary(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return false;

        string extension = Path.GetExtension(filePath);
        if (!string.IsNullOrEmpty(extension) && IgnoredExtensions.Contains(extension))
        {
            return true;
        }

        return IgnoredFileSubstrings.Any(sub => filePath.Contains(sub, StringComparison.OrdinalIgnoreCase));
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
        bool isCurrentFileIgnored = false;

        while ((line = reader.ReadLine()) != null)
        {
            if (line.StartsWith("diff --git a/"))
            {
                var parts = line.Split(' ');
                string detectedPath = parts.Length >= 4 && parts[3].StartsWith("b/")
                    ? parts[3][2..]
                    : (parts.Length >= 3 && parts[2].StartsWith("a/") ? parts[2][2..] : string.Empty);

                isCurrentFileIgnored = IsGeneratedOrBinary(detectedPath);

                if (currentChunk.Length > 0 && currentChunk.Length + line.Length > maxLength)
                {
                    chunks.Add(currentChunk.ToString());
                    currentChunk.Clear();
                }

                if (isCurrentFileIgnored)
                {
                    currentChunk.AppendLine(line);
                    currentChunk.AppendLine($"[SKIPPED: Auto-generated/binary payload for {detectedPath}]");
                    continue;
                }
            }

            if (!isCurrentFileIgnored)
            {
                currentChunk.AppendLine(line);
            }
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

            FILES IN THIS CHUNK WITH STATUS:
            {filesHeader}

            INSTRUCTIONS:
            1. Summarize what was modified, added, or deleted in 1-2 concise sentences.
            2. CRITICAL ACCURACY RULES:
               - If a file is marked [MODIFIED], describe changes/fixes to existing code. NEVER state that the file is created or added, even if lines are inserted (+).
               - Only describe a file as new or created if it is explicitly marked [ADDED].
               - Describe files marked [DELETED] as removed.
               - Describe files marked [RENAMED] as renamed or moved.
               - Ignore lines mentioning [SKIPPED: Auto-generated/binary payload].
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

    private async Task<string> GenerateFinalCommitAsync(
        string aggregatedContext,
        string diffStat,
        List<FileDiffStatus> fileStatuses,
        CancellationToken ct)
    {
        string fileListText = string.Join("\n", fileStatuses.Select(f => $"- [{f.Status.ToUpperInvariant()}] {f.Path}"));
        string statSection = string.IsNullOrWhiteSpace(diffStat)
            ? string.Empty
            : $"""
            
            DIFF VOLUME STATISTICS:
            {diffStat}
            """;

        string prompt = $"""
            You are an expert developer generating a Conventional Commit message.
            Strictly adhere to the provided file statuses, diff volume statistics, and context.

            FILE CHANGE REGISTRY:
            {fileListText}{statSection}

            RULES:
            1. MUST be exclusively in English.
            2. First line (Title): Format as type(scope): description. STRICTLY under 72 characters.
            3. CRITICAL STATUS ACCURACY RULES:
               - NEVER label a file as 'created', 'introduced', or 'added' if its status is [MODIFIED]. Use verbs like 'update', 'refactor', 'enhance', 'fix'.
               - ONLY treat files with status [ADDED] as newly created files.
               - Files with status [DELETED] must be described as removed or deleted.
               - Files with status [RENAMED] must be described as renamed or moved.
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