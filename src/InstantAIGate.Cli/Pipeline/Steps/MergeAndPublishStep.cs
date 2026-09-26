namespace InstantAIGate.Cli.Pipeline.Steps;

using InstantAIGate.Cli.Configuration;
using InstantAIGate.Cli.Core;
using InstantAIGate.Cli.Services;
using InstantAIGate.Cli.State;
using InstantAIGate.Core.Dtos.Inference;
using Microsoft.Extensions.Options;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

public class MergeAndPublishStep : IPipelineStep
{
    private readonly IGitService _gitService;
    private readonly IProcessRunner _processRunner;
    private readonly IGatewayClient _gatewayClient;
    private readonly ReleasePipelineSettings _settings;

    public string Name => "Merge to Target Branch, Tag, and Publish Release";

    public MergeAndPublishStep(
        IGitService gitService,
        IProcessRunner processRunner,
        IGatewayClient gatewayClient,
        IOptions<ReleasePipelineSettings> settings)
    {
        _gitService = gitService;
        _processRunner = processRunner;
        _gatewayClient = gatewayClient;
        _settings = settings.Value;
    }

    public async Task ExecuteAsync(PipelineContext context, CancellationToken cancellationToken)
    {
        await Spectre.Console.AnsiConsole.Status()
            .Spinner(Spectre.Console.Spinner.Known.Dots)
            .StartAsync("Executing Git Merge and GitHub Release...", async ctx =>
            {
                string originalBranch = await _gitService.GetCurrentBranchAsync(cancellationToken);
                string targetBranch = _settings.TargetBranch;
                string versionTag = $"v{context.NewVersion}";

                ctx.Status("Committing version bump...");
                await _gitService.AddAllAsync(cancellationToken);
                await _gitService.CommitAsync($"chore(release): prepare version {versionTag}", cancellationToken);
                await _gitService.PushBranchAsync(originalBranch, cancellationToken);

                ctx.Status($"Merging into {targetBranch}...");
                await _gitService.CheckoutAsync(targetBranch, cancellationToken);
                await _gitService.PullAsync(cancellationToken);
                await _gitService.MergeNoFastForwardAsync(originalBranch, $"chore(release): merge {originalBranch} into {targetBranch} for {versionTag}", cancellationToken);
                await _gitService.PushBranchAsync(targetBranch, cancellationToken);

                ctx.Status("Generating AI Release Notes...");
                string lastTag = string.Empty;
                try { lastTag = await _gitService.GetLatestTagAsync(cancellationToken); } catch { }
                string gitLog = await _gitService.GetGitLogAsync(lastTag, cancellationToken);
                string releaseNotes = await GenerateChangelogAsync(gitLog, cancellationToken);

                ctx.Status("Publishing Release to GitHub...");
                await _gitService.CreateAndPushTagAsync(versionTag, cancellationToken);
                await CreateGitHubReleaseAsync(versionTag, releaseNotes, context.NewVersion.Contains('-'), cancellationToken);

                ctx.Status("Syncing working branch...");
                await _gitService.CheckoutAsync(originalBranch, cancellationToken);
                await _gitService.MergeAsync(targetBranch, $"chore(sync): merge {targetBranch} back to {originalBranch}", cancellationToken);
                await _gitService.PushBranchAsync(originalBranch, cancellationToken);
            });
    }

    private async Task<string> GenerateChangelogAsync(string gitLog, CancellationToken ct)
    {
        string prompt = $@"Analyze the following git commit log and generate a professional release changelog.
RULES:
1. MUST be exclusively in English.
2. Group changes logically (e.g., Features, Bug Fixes, Chores).
3. Keep descriptions concise.
4. Provide ONLY the changelog text. Do not use markdown code blocks or quotes.

Commit Log:
{gitLog}";

        var messages = new List<ChatMessage> { new ChatMessage("user", prompt) };
        var sb = new StringBuilder();
        await foreach (var chunk in _gatewayClient.StreamChatAsync(_settings.AiModelId, messages, ct))
        {
            sb.Append(chunk);
        }
        return sb.ToString().Trim(' ', '\n', '\r', '`', '"', '\'');
    }

    private async Task CreateGitHubReleaseAsync(string tagName, string notes, bool isPrerelease, CancellationToken ct)
    {
        string tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, notes, ct);
            string prereleaseFlag = isPrerelease ? "--prerelease" : "--latest";
            string args = $"release create {tagName} -t \"Release {tagName}\" -F \"{tempFile}\" {prereleaseFlag}";

            var dir = new DirectoryInfo(Environment.CurrentDirectory);
            while (dir != null && dir.GetFiles("InstantAIGate.sln").Length == 0) dir = dir.Parent;
            string workingDir = dir?.FullName ?? Environment.CurrentDirectory;

            int exitCode = await _processRunner.ExecuteAsync("gh", args, workingDir, silent: false, ct);
            if (exitCode != 0)
            {
                throw new InvalidOperationException($"GitHub CLI failed to create the release. Exit code: {exitCode}");
            }
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}