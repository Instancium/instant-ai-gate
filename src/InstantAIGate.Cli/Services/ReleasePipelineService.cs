namespace InstantAIGate.Cli.Services;

using InstantAIGate.Cli.Configuration;
using InstantAIGate.Cli.Core;
using InstantAIGate.Core.Dtos.Inference;
using Microsoft.Extensions.Options;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

public class ReleasePipelineService
{
    private readonly IGatewayClient _gatewayClient;
    private readonly ReleasePipelineSettings _settings;

    private const string ServerProjectFile = "src/InstantAIGate.Server/InstantAIGate.Server.csproj";
    private const string SolutionFile = "InstantAIGate.sln";

    public ReleasePipelineService(IGatewayClient gatewayClient, IOptions<ReleasePipelineSettings> options)
    {
        _gatewayClient = gatewayClient;
        _settings = options.Value;
    }

    public async Task ExecutePipelineAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Step 1: Pre-flight checks and branch selection
            EnsureCleanWorkingDirectory();
            await ExecuteProcessAsync("git", "fetch --all", cancellationToken);
            string selectedBranch = await SelectWorkingBranchAsync(cancellationToken);
            await ExecuteProcessAsync("git", $"checkout {selectedBranch}", cancellationToken);
            await ExecuteProcessAsync("git", "pull", cancellationToken);

            // Step 2: Version and Isolation
            string currentVersion = GetCurrentVersion();
            AnsiConsole.MarkupLine($"\nCurrent version: [cyan]{currentVersion}[/]");
            var bumpType = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Select version bump type:")
                    .AddChoices(new[] { "patch", "minor", "major", "prerelease", "abort" }));

            if (bumpType == "abort") return;

            string newVersion = CalculateNewVersion(currentVersion, bumpType);
            bool isPreRelease = newVersion.Contains('-');
            string releaseBranch = $"{_settings.ReleaseBranchPrefix}{newVersion}";

            await ExecuteProcessAsync("git", $"checkout -b {releaseBranch}", cancellationToken);
            UpdateProjectVersion(newVersion);

            // Step 3: Fail-Fast Validation
            await AnsiConsole.Status().StartAsync("Running unit tests...", async ctx =>
            {
                int exitCode = await ExecuteProcessWithReturnCodeAsync("dotnet", $"test {SolutionFile} -c Release", cancellationToken);
                if (exitCode != 0)
                {
                    throw new InvalidOperationException("Unit tests failed. Pipeline aborted. Branch not pushed.");
                }
            });

            // Step 4: Generate Release Notes via AI
            string releaseNotes = await AnsiConsole.Status().StartAsync("Generating release notes via AI...", async ctx =>
            {
                return await GenerateReleaseNotesAsync(cancellationToken);
            });
            AnsiConsole.MarkupLine("\n[green]Generated Release Notes:[/]\n" + releaseNotes + "\n");

            // Step 5: Commit and Create PR
            await AnsiConsole.Status().StartAsync("Committing and creating Pull Request...", async ctx =>
            {
                await ExecuteProcessAsync("git", "add .", cancellationToken);
                string commitMsg = $"chore(release): prepare version v{newVersion}";
                await ExecuteProcessAsync("git", $"commit -m \"{commitMsg}\"", cancellationToken);
                await ExecuteProcessAsync("git", $"push -u origin {releaseBranch}", cancellationToken);

                string tempNotesFile = Path.GetTempFileName();
                await File.WriteAllTextAsync(tempNotesFile, releaseNotes, cancellationToken);
                string prTitle = $"chore(release): publish version v{newVersion}";
                await ExecuteProcessAsync("gh", $"pr create --base {_settings.TargetBranch} --head {releaseBranch} --title \"{prTitle}\" --body-file \"{tempNotesFile}\"", cancellationToken);
                File.Delete(tempNotesFile);
            });

            // Step 6: Interactive Pause
            AnsiConsole.MarkupLine($"[yellow]Pull Request created. Please review, approve, and merge it into '{_settings.TargetBranch}' using the web interface.[/]");
            AnsiConsole.Prompt(new TextPrompt<string>("Press [green]ENTER[/] after the PR is successfully merged...").AllowEmpty());

            // Step 7: Sync and Tag
            await AnsiConsole.Status().StartAsync("Syncing main and tagging...", async ctx =>
            {
                await ExecuteProcessAsync("git", $"checkout {_settings.TargetBranch}", cancellationToken);
                await ExecuteProcessAsync("git", "pull", cancellationToken);
                await ExecuteProcessAsync("git", $"tag v{newVersion}", cancellationToken);
                await ExecuteProcessAsync("git", $"push origin v{newVersion}", cancellationToken);
            });

            // Step 8: Build Final Artifacts
            string zipPath = string.Empty;
            await AnsiConsole.Status().StartAsync("Building release artifacts...", async ctx =>
            {
                ctx.Status("Building Windows self-contained artifact...");
                zipPath = await BuildWindowsAssetAsync(newVersion, cancellationToken);

                ctx.Status("Building Linux GHCR image...");
                await BuildDockerImageAsync(newVersion, isPreRelease, cancellationToken);
            });

            // Step 9: Final Publish
            await AnsiConsole.Status().StartAsync("Publishing release...", async ctx =>
            {
                string tempNotesFile = Path.GetTempFileName();
                await File.WriteAllTextAsync(tempNotesFile, releaseNotes, cancellationToken);
                string preReleaseFlag = isPreRelease ? "--prerelease" : "--latest";

                ctx.Status("Creating GitHub Release...");
                await ExecuteProcessAsync("gh", $"release create v{newVersion} -t \"Release v{newVersion}\" -F \"{tempNotesFile}\" {preReleaseFlag}", cancellationToken);

                ctx.Status("Uploading Windows asset...");
                await ExecuteProcessAsync("gh", $"release upload v{newVersion} \"{zipPath}\"", cancellationToken);
                File.Delete(tempNotesFile);

                ctx.Status("Pushing GHCR images...");
                await PushDockerImageAsync(newVersion, isPreRelease, cancellationToken);

                ctx.Status("Cleaning up local release branch...");
                await ExecuteProcessAsync("git", $"branch -d {releaseBranch}", cancellationToken);
            });

            AnsiConsole.MarkupLine($"\n[bold green]Release v{newVersion} published successfully![/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"\n[bold red]Pipeline Error:[/] {ex.Message}");
        }
    }

    private void EnsureCleanWorkingDirectory()
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "status --porcelain",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        process.Start();
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        if (!string.IsNullOrWhiteSpace(output))
        {
            throw new InvalidOperationException("Working directory is not clean. Commit or stash your changes first.");
        }
    }

    private async Task<string> SelectWorkingBranchAsync(CancellationToken cancellationToken)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "branch --sort=-committerdate --format=\"%(refname:short)\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        process.Start();
        string output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        var branches = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(b => _settings.AllowedBranchPrefixes.Any(prefix => b.StartsWith(prefix) || b == prefix))
            .Take(5)
            .ToList();

        if (branches.Count == 0)
        {
            throw new InvalidOperationException("No suitable working branches found matching the allowed prefixes.");
        }

        return AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Select the source branch for the release:")
                .AddChoices(branches));
    }

    private string GetCurrentVersion()
    {
        var content = File.ReadAllText(ServerProjectFile);
        var match = Regex.Match(content, @"<Version>(.*?)</Version>");
        return match.Success ? match.Groups[1].Value : "1.0.0";
    }

    private void UpdateProjectVersion(string newVersion)
    {
        var content = File.ReadAllText(ServerProjectFile);
        if (Regex.IsMatch(content, @"<Version>.*?</Version>"))
        {
            content = Regex.Replace(content, @"<Version>.*?</Version>", $"<Version>{newVersion}</Version>");
        }
        else
        {
            content = content.Replace("</PropertyGroup>", $"  <Version>{newVersion}</Version>\n  </PropertyGroup>");
        }
        File.WriteAllText(ServerProjectFile, content);
    }

    private string CalculateNewVersion(string currentVersion, string bumpType)
    {
        var cleanVersion = currentVersion.Split('-')[0];
        var parts = cleanVersion.Split('.');
        int major = int.Parse(parts[0]);
        int minor = int.Parse(parts[1]);
        int patch = int.Parse(parts[2]);

        return bumpType switch
        {
            "major" => $"{major + 1}.0.0",
            "minor" => $"{major}.{minor + 1}.0",
            "patch" => $"{major}.{minor}.{patch + 1}",
            "prerelease" => $"{major}.{minor}.{patch + 1}-rc.1",
            _ => currentVersion
        };
    }

    private async Task<string> GenerateReleaseNotesAsync(CancellationToken cancellationToken)
    {
        string lastTag = await GetCommandOutputAsync("git", "describe --tags --abbrev=0", cancellationToken);
        string gitLog = string.IsNullOrWhiteSpace(lastTag)
            ? await GetCommandOutputAsync("git", "log --oneline -n 20", cancellationToken)
            : await GetCommandOutputAsync("git", $"log {lastTag.Trim()}..HEAD --oneline", cancellationToken);

        if (string.IsNullOrWhiteSpace(gitLog)) return "Maintenance and dependency updates.";

        string prompt = $@"Analyze the following git commit log and generate a professional release description in English.
Group the changes logically (e.g., Features, Bug Fixes, Chores).
Keep it concise and professional. Do not use markdown headers larger than h3.
Commit Log:
{gitLog}";

        var messages = new List<ChatMessage> { new ChatMessage("user", prompt) };
        var sb = new StringBuilder();

        await _gatewayClient.LoadModelAsync(_settings.AiModelId, cancellationToken);
        await foreach (var chunk in _gatewayClient.StreamChatAsync(_settings.AiModelId, messages, cancellationToken))
        {
            sb.Append(chunk);
        }

        return sb.ToString();
    }

    private async Task<string> BuildWindowsAssetAsync(string version, CancellationToken cancellationToken)
    {
        string outputDir = Path.Combine(Path.GetTempPath(), $"InstantAIGate_Build_v{version}");
        if (Directory.Exists(outputDir)) Directory.Delete(outputDir, true);

        string buildArgs = $"publish {ServerProjectFile} -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o \"{outputDir}\"";
        await ExecuteProcessAsync("dotnet", buildArgs, cancellationToken);

        string nativeSourceDir = "src/InstantAIGate.Native/runtimes/win-x64";
        if (Directory.Exists(nativeSourceDir))
        {
            foreach (var file in Directory.GetFiles(nativeSourceDir, "*.dll"))
            {
                File.Copy(file, Path.Combine(outputDir, Path.GetFileName(file)), true);
            }
        }

        string zipPath = Path.Combine(Directory.GetCurrentDirectory(), $"InstantAIGate-Server-win-x64-v{version}.zip");
        if (File.Exists(zipPath)) File.Delete(zipPath);

        System.IO.Compression.ZipFile.CreateFromDirectory(outputDir, zipPath);
        return zipPath;
    }

    private async Task BuildDockerImageAsync(string version, bool isPreRelease, CancellationToken cancellationToken)
    {
        string imageName = "ghcr.io/your-org/instantaigate-server";
        await ExecuteProcessAsync("docker", $"build -f deploy/docker/Dockerfile -t {imageName}:v{version} .", cancellationToken);
        if (!isPreRelease)
        {
            await ExecuteProcessAsync("docker", $"tag {imageName}:v{version} {imageName}:latest", cancellationToken);
        }
    }

    private async Task PushDockerImageAsync(string version, bool isPreRelease, CancellationToken cancellationToken)
    {
        string imageName = "ghcr.io/your-org/instantaigate-server";
        await ExecuteProcessAsync("docker", $"push {imageName}:v{version}", cancellationToken);
        if (!isPreRelease)
        {
            await ExecuteProcessAsync("docker", $"push {imageName}:latest", cancellationToken);
        }
    }

    private async Task ExecuteProcessAsync(string fileName, string arguments, CancellationToken cancellationToken)
    {
        int exitCode = await ExecuteProcessWithReturnCodeAsync(fileName, arguments, cancellationToken);
        if (exitCode != 0)
        {
            throw new InvalidOperationException($"Command '{fileName} {arguments}' failed with exit code {exitCode}.");
        }
    }

    private async Task<int> ExecuteProcessWithReturnCodeAsync(string fileName, string arguments, CancellationToken cancellationToken)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode;
    }

    private async Task<string> GetCommandOutputAsync(string fileName, string arguments, CancellationToken cancellationToken)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        string output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return output.Trim();
    }
}