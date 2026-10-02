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

    private static readonly HashSet<string> IgnoredExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".lock", ".min.js", ".min.css", ".map", ".svg", ".png", ".jpg", ".jpeg", ".gif", ".ico",
        ".pdf", ".dll", ".so", ".exe", ".bin", ".gguf", ".zip", ".tar", ".gz", ".wasm", ".bundle.js",
        ".Designer.cs", ".g.cs"
    };

    private static readonly string[] IgnoredFileSubstrings =
    {
        "package-lock.json", "pnpm-lock.yaml", "yarn.lock", "obj/Debug/", "obj/Release/",
        "bin/Debug/", "bin/Release/", ".min."
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

        var modelDetails = await _gatewayClient.GetActiveModelDetailsAsync(ct);
        int contextSize = modelDetails.ContextSize > 0 ? modelDetails.ContextSize : 4096;

        // Formula: SafeChunkChars = (ContextSize - SystemPromptTokens - ReserveTokens) * 3.2
        const int systemPromptTokens = 400;
        const int reserveTokens = 512;
        int usableTokens = Math.Max(512, contextSize - systemPromptTokens - reserveTokens);
        int safeChunkChars = (int)(usableTokens * 3.2);

        var totalFileStatuses = ExtractFileStatuses(rawDiff);

        if (rawDiff.Length <= safeChunkChars)
        {
            return await GenerateDirectCommitAsync(rawDiff, diffStat, totalFileStatuses, ct);
        }

        AnsiConsole.MarkupLine($"[dim]Diff exceeds single chunk capacity ({rawDiff.Length} chars > {safeChunkChars} safe limit). Engaging Checkpointed Map-Reduce...[/]");

        string pipelineSessionId = $"diff-pipeline-{Guid.NewGuid():N}";
        try
        {
            // 1. Initialize session with persistent system prompt instructions
            string systemPrompt = """
            You are an expert source code reviewer analyzing git diffs for a release commit.
            Your task is to analyze diff chunks and provide concise technical summaries.
            CRITICAL STATUS ACCURACY RULES:
            - If a file is marked [MODIFIED], describe changes/fixes to existing code. NEVER state that it was added or created.
            - Only describe a file as new if marked [ADDED].
            - Describe files marked [DELETED] as removed, and [RENAMED] as renamed or moved.
            - Provide 1-2 dense sentences without markdown fences or headers.
            """;

            var initMessage = new ChatMessage("system", systemPrompt);
            await ConsumeStreamAsync(pipelineSessionId, initMessage, ct);

            // 2. Prefix Checkpoint: Anchor system prompt in KV-cache
            int sysCheckpoint = await _gatewayClient.GetSessionTokenCountAsync(pipelineSessionId, ct);

            var chunks = ChunkDiffByFiles(rawDiff, safeChunkChars);
            var partialSummaries = new List<string>();

            // 3. Map Phase with Rollback: Evaluate chunks without re-tokenizing system prompt
            for (int i = 0; i < chunks.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                AnsiConsole.MarkupLine($"[dim]Analyzing part {i + 1}/{chunks.Count} with KV-cache reuse...[/]");

                var chunkStatuses = ExtractFileStatuses(chunks[i]);
                string filesHeader = chunkStatuses.Count > 0
                    ? string.Join("\n", chunkStatuses.Select(f => $"- [{f.Status}] {f.Path}"))
                    : "- [MODIFIED] Unknown files";

                string chunkPrompt = $"""
                FILES IN THIS CHUNK WITH STATUS:
                {filesHeader}

                DIFF:
                {chunks[i]}
                """;

                var userMessage = new ChatMessage("user", chunkPrompt);
                string summary = await StreamSummaryAsync(pipelineSessionId, userMessage, ct);

                if (!string.IsNullOrWhiteSpace(summary))
                {
                    partialSummaries.Add(summary);
                }

                // Deterministically evict the diff chunk from KV-cache while retaining system prefix
                await _gatewayClient.RollbackSessionAsync(pipelineSessionId, sysCheckpoint, ct);
            }

            // 4. Reduce Phase: Hierarchical Tree-Reduce if aggregated summaries exceed window
            AnsiConsole.MarkupLine("[dim]Hierarchically aggregating summaries into final commit message...[/]");
            string aggregatedSummary = await ReduceSummariesTreeAsync(pipelineSessionId, sysCheckpoint, partialSummaries, safeChunkChars, ct);

            return await GenerateFinalCommitOnSessionAsync(pipelineSessionId, aggregatedSummary, diffStat, totalFileStatuses, ct);
        }
        finally
        {
            await _gatewayClient.EndSessionAsync(pipelineSessionId, CancellationToken.None);
        }
    }

    private async Task<string> ReduceSummariesTreeAsync(
        string sessionId,
        int sysCheckpoint,
        List<string> summaries,
        int maxCharLimit,
        CancellationToken ct)
    {
        string joined = string.Join("\n- ", summaries);
        if (joined.Length <= maxCharLimit || summaries.Count <= 3)
        {
            return joined;
        }

        AnsiConsole.MarkupLine($"[yellow]Aggregated summaries exceed window ({joined.Length} chars). Executing intermediate tree reduction...[/]");

        var reducedList = new List<string>();
        const int groupSize = 3;

        for (int i = 0; i < summaries.Count; i += groupSize)
        {
            ct.ThrowIfCancellationRequested();
            var group = summaries.Skip(i).Take(groupSize).ToList();
            string groupText = string.Join("\n- ", group);

            string prompt = $"""
            Combine and compress the following partial review notes into a dense, cohesive summary.
            Keep all architectural details but remove redundant phrasing.
            NOTES:
            - {groupText}
            """;

            var msg = new ChatMessage("user", prompt);
            string compressed = await StreamSummaryAsync(sessionId, msg, ct);
            reducedList.Add(compressed);

            await _gatewayClient.RollbackSessionAsync(sessionId, sysCheckpoint, ct);
        }

        return await ReduceSummariesTreeAsync(sessionId, sysCheckpoint, reducedList, maxCharLimit, ct);
    }

    private async Task<string> GenerateFinalCommitOnSessionAsync(
        string sessionId,
        string aggregatedContext,
        string diffStat,
        List<FileDiffStatus> fileStatuses,
        CancellationToken ct)
    {
        string fileListText = BuildCompactFileRegistry(fileStatuses);
        string compactDiffStat = CompressDiffStat(diffStat);

        string statSection = string.IsNullOrWhiteSpace(compactDiffStat) ? string.Empty : $"""
        DIFF VOLUME STATISTICS:
        {compactDiffStat}
        """;

        string prompt = $"""
        You are an expert developer generating a Conventional Commit message. Strictly adhere to the provided file statuses, diff volume statistics, and context.

        FILE CHANGE OVERVIEW:
        {fileListText}
        {statSection}

        RULES:
        1. MUST be exclusively in English.
        2. First line (Title): Format as type(scope): description.
           - HARD LIMIT: Target 50-65 characters. Absolute maximum is 72 characters.
           - Be concise, direct, and imperative (e.g., 'feat(server): add startup worker tests').
        3. CRITICAL STATUS ACCURACY RULES:
           - NEVER label a file as 'created', 'introduced', or 'added' if its status is [MODIFIED]. Use verbs like 'update', 'refactor', 'enhance', 'fix'.
           - ONLY treat files with status [ADDED] as newly created files.
           - Files with status [DELETED] must be described as removed or deleted.
           - Files with status [RENAMED] must be described as renamed or moved.
        4. FILE TYPE AND SCOPE RULE:
           - If ONLY documentation files (.md, .txt) are changed, type MUST be 'docs'.
           - Derive the primary scope from the most affected module indicated in the overview.
        5. Second line: MUST be completely blank.
        6. Third line onwards (Body): Provide a concise bulleted list detailing WHAT was changed, preserving exact file statuses.

        Context:
        {aggregatedContext}
        """;

        var deltaMessage = new ChatMessage("user", prompt);
        string rawResult = await StreamSummaryAsync(sessionId, deltaMessage, ct);
        return SanitizeCommitMessage(rawResult);
    }

    private async Task<string> GenerateDirectCommitAsync(
        string rawDiff,
        string diffStat,
        List<FileDiffStatus> fileStatuses,
        CancellationToken ct)
    {
        string sessionId = $"diff-direct-{Guid.NewGuid():N}";
        try
        {
            return await GenerateFinalCommitOnSessionAsync(sessionId, rawDiff, diffStat, fileStatuses, ct);
        }
        finally
        {
            await _gatewayClient.EndSessionAsync(sessionId, CancellationToken.None);
        }
    }

    private async Task<string> StreamSummaryAsync(string sessionId, ChatMessage message, CancellationToken ct)
    {
        var sb = new StringBuilder();
        await foreach (var token in _gatewayClient.StreamChatAsync(sessionId, _modelId, message, ct))
        {
            sb.Append(token);
        }
        return sb.ToString().Trim(' ', '\r', '\n', '`', '\'', '"');
    }

    private async Task ConsumeStreamAsync(string sessionId, ChatMessage message, CancellationToken ct)
    {
        await foreach (var _ in _gatewayClient.StreamChatAsync(sessionId, _modelId, message, ct))
        {
            // Discard initial prompt echo
        }
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
            if (line.StartsWith("diff --git a"))
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
        string currentFileName = string.Empty;

        while ((line = reader.ReadLine()) != null)
        {
            if (line.StartsWith("diff --git a"))
            {
                var parts = line.Split(' ');
                currentFileName = parts.Length >= 4 && parts[3].StartsWith("b/")
                    ? parts[3][2..]
                    : (parts.Length >= 3 && parts[2].StartsWith("a/") ? parts[2][2..] : string.Empty);

                isCurrentFileIgnored = IsGeneratedOrBinary(currentFileName);

                if (currentChunk.Length > 0 && currentChunk.Length + line.Length > maxLength)
                {
                    chunks.Add(currentChunk.ToString());
                    currentChunk.Clear();
                }

                if (isCurrentFileIgnored)
                {
                    currentChunk.AppendLine(line);
                    currentChunk.AppendLine($"[SKIPPED: Auto-generated/binary payload for {currentFileName}]");
                    continue;
                }
            }

            if (!isCurrentFileIgnored)
            {
                if (currentChunk.Length > 0 && currentChunk.Length + line.Length > maxLength)
                {
                    chunks.Add(currentChunk.ToString());
                    currentChunk.Clear();

                    if (!string.IsNullOrEmpty(currentFileName))
                    {
                        currentChunk.AppendLine($"[... CONTINUATION OF DIFF FOR FILE: {currentFileName} ...]");
                    }
                }

                currentChunk.AppendLine(line);
            }
        }

        if (currentChunk.Length > 0)
        {
            chunks.Add(currentChunk.ToString());
        }

        return chunks;
    }

    private static string BuildCompactFileRegistry(List<FileDiffStatus> fileStatuses)
    {
        const int maxFlatDisplay = 20;
        if (fileStatuses.Count <= maxFlatDisplay)
        {
            return string.Join("\n", fileStatuses.Select(f => $"- [{f.Status.ToUpperInvariant()}] {f.Path}"));
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Total files changed: {fileStatuses.Count}. Module Breakdown:");

        var groups = fileStatuses
            .GroupBy(f =>
            {
                var parts = f.Path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 2 && parts[0].Equals("src", StringComparison.OrdinalIgnoreCase))
                {
                    return $"{parts[0]}/{parts[1]}";
                }
                return parts.Length > 1 ? parts[0] : "Root";
            })
            .OrderByDescending(g => g.Count());

        foreach (var group in groups)
        {
            int modifiedCount = group.Count(f => f.Status == "MODIFIED");
            int addedCount = group.Count(f => f.Status == "ADDED");
            int deletedCount = group.Count(f => f.Status == "DELETED");
            int renamedCount = group.Count(f => f.Status == "RENAMED");

            var statusParts = new List<string>();
            if (modifiedCount > 0) statusParts.Add($"{modifiedCount} modified");
            if (addedCount > 0) statusParts.Add($"{addedCount} added");
            if (deletedCount > 0) statusParts.Add($"{deletedCount} deleted");
            if (renamedCount > 0) statusParts.Add($"{renamedCount} renamed");

            sb.AppendLine($"- {group.Key}: {string.Join(", ", statusParts)}");
        }

        return sb.ToString().TrimEnd();
    }

    private static string CompressDiffStat(string diffStat)
    {
        if (string.IsNullOrWhiteSpace(diffStat)) return string.Empty;

        var lines = diffStat.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length <= 12) return diffStat;

        var topEntries = lines.Take(10);
        string summaryLine = lines.Last();

        var sb = new StringBuilder();
        foreach (var line in topEntries)
        {
            sb.AppendLine(line);
        }
        sb.AppendLine($"... ({lines.Length - 11} more files)");
        sb.AppendLine(summaryLine);

        return sb.ToString().TrimEnd();
    }

    private static string SanitizeCommitMessage(string rawMessage)
    {
        if (string.IsNullOrWhiteSpace(rawMessage)) return rawMessage;

        var lines = rawMessage.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
        string title = lines[0].Trim();

        const int maxHeaderLength = 72;
        const int truncateAt = 69;

        if (title.Length > maxHeaderLength)
        {
            title = string.Concat(title.AsSpan(0, truncateAt), "...");
        }

        if (lines.Length <= 1) return title;

        var bodyLines = lines.Skip(1).ToList();
        while (bodyLines.Count > 0 && string.IsNullOrWhiteSpace(bodyLines[0]))
        {
            bodyLines.RemoveAt(0);
        }

        if (bodyLines.Count == 0) return title;

        var builder = new StringBuilder();
        builder.AppendLine(title);
        builder.AppendLine();

        for (int i = 0; i < bodyLines.Count; i++)
        {
            builder.AppendLine(bodyLines[i]);
        }

        return builder.ToString().TrimEnd();
    }
}