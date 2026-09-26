namespace InstantAIGate.Cli.Services;

using InstantAIGate.Cli.Configuration;
using InstantAIGate.Cli.Core;
using InstantAIGate.Cli.State;
using InstantAIGate.Core.Dtos.Inference;
using Microsoft.Extensions.Options;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

public class ReleasePipelineService
{
    private readonly IGatewayClient _gatewayClient;
    private readonly ReleasePipelineSettings _settings;

    private string ServerProjectFile => Path.Combine(GetSolutionRootDirectory(), "src", "InstantAIGate.Server", "InstantAIGate.Server.csproj");
    private string SolutionFile => Path.Combine(GetSolutionRootDirectory(), "InstantAIGate.sln");
    private string StateFile => Path.Combine(Path.GetTempPath(), "InstantAIGate_Pipeline_State.json");


    private PipelineState LoadOrCreateState()
    {
        if (File.Exists(StateFile))
        {
            try
            {
                var json = File.ReadAllText(StateFile);
                return JsonSerializer.Deserialize<PipelineState>(json) ?? new PipelineState();
            }
            catch { return new PipelineState(); }
        }
        return new PipelineState();
    }

    private void SaveState(PipelineState state)
    {
        var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(StateFile, json);
    }

    private void ClearState()
    {
        if (File.Exists(StateFile)) File.Delete(StateFile);
    }

    public ReleasePipelineService(IGatewayClient gatewayClient, IOptions<ReleasePipelineSettings> options)
    {
        _gatewayClient = gatewayClient;
        _settings = options.Value;
    }


    public async Task ExecutePipelineAsync(CancellationToken cancellationToken)
    {
        try
        {
            var state = LoadOrCreateState();

            if (state.LastCompletedStep > 0)
            {
                AnsiConsole.MarkupLine($"[yellow]Found an interrupted pipeline for version v{state.NewVersion} (Last completed step: {state.LastCompletedStep}).[/]");
                var resume = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title("Resume pipeline or start fresh?")
                        .AddChoices("Resume", "Start Fresh"));

                if (resume == "Start Fresh")
                {
                    ClearState();
                    state = new PipelineState();
                }
            }

            // ==========================================
            // Step 1: Pre-flight checks and branch selection
            // ==========================================
            if (state.LastCompletedStep < 1)
            {
                await AnsiConsole.Status().StartAsync("Checking repository state...", async ctx =>
                {
                    EnsureCleanWorkingDirectory();
                    ctx.Status("Fetching latest changes from origin...");
                    await ExecuteProcessAsync("git", "fetch --all", cancellationToken);
                });

                state.SelectedBranch = await SelectWorkingBranchAsync(cancellationToken);

                await AnsiConsole.Status().StartAsync($"Preparing branch '{state.SelectedBranch}'...", async ctx =>
                {
                    ctx.Status($"Switching to {state.SelectedBranch}...");
                    await ExecuteProcessAsync("git", $"checkout {state.SelectedBranch}", cancellationToken);
                    ctx.Status("Pulling latest commits...");
                    await ExecuteProcessAsync("git", "pull", cancellationToken);
                });

                state.LastCompletedStep = 1;
                SaveState(state);
            }

            // ==========================================
            // Step 2: Version and Isolation
            // ==========================================
            if (state.LastCompletedStep < 2)
            {
                string currentVersion = GetCurrentVersion();
                AnsiConsole.MarkupLine($"\nCurrent version: [cyan]{currentVersion}[/]");
                var bumpType = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title("Select version bump type:")
                        .AddChoices(new[] { "patch", "minor", "major", "prerelease", "abort" }));

                if (bumpType == "abort") return;

                state.NewVersion = CalculateNewVersion(currentVersion, bumpType);
                state.ReleaseBranch = $"{_settings.ReleaseBranchPrefix}{state.NewVersion}";

                await AnsiConsole.Status().StartAsync($"Creating release branch '{state.ReleaseBranch}'...", async ctx =>
                {
                    await ExecuteProcessAsync("git", $"checkout -b {state.ReleaseBranch}", cancellationToken);
                    UpdateProjectVersion(state.NewVersion);
                });

                state.LastCompletedStep = 2;
                SaveState(state);
            }

            bool isPreRelease = state.NewVersion.Contains('-');
            string tempNotesFile = Path.Combine(Path.GetTempPath(), $"RELEASE_NOTES_v{state.NewVersion}.md");

            try
            {
                // ==========================================
                // Step 3: Fail-Fast Validation
                // ==========================================
                if (state.LastCompletedStep < 3)
                {
                    await AnsiConsole.Status().StartAsync("Running unit tests...", async ctx =>
                    {
                        int exitCode = await ExecuteProcessWithReturnCodeAsync("dotnet", $"test \"{SolutionFile}\" -c Release", cancellationToken);
                        if (exitCode != 0) throw new InvalidOperationException("Unit tests failed.");
                    });

                    state.LastCompletedStep = 3;
                    SaveState(state);
                }

                // ==========================================
                // Step 4: Generate Release Notes via AI
                // ==========================================
                if (state.LastCompletedStep < 4)
                {
                    state.ReleaseNotes = await AnsiConsole.Status().StartAsync("Generating release notes via AI...", async ctx =>
                    {
                        return await GenerateReleaseNotesAsync(cancellationToken);
                    });
                    await File.WriteAllTextAsync(tempNotesFile, state.ReleaseNotes, cancellationToken);

                    AnsiConsole.MarkupLine("\n[yellow]Opening Release Notes in your default text editor...[/]");
                    Process.Start(new ProcessStartInfo(tempNotesFile) { UseShellExecute = true });

                    AnsiConsole.MarkupLine("\n[cyan]Take your time to manual test the application or tweak the release notes.[/]");
                    var userAction = AnsiConsole.Prompt(
                        new SelectionPrompt<string>()
                            .Title("Are you ready to commit and create the Pull Request?")
                            .AddChoices(new[] { "Approve (Commit & Create PR)", "Abort (Rollback changes)" }));

                    if (userAction == "Abort (Rollback changes)")
                    {
                        throw new InvalidOperationException("Pipeline aborted manually by user during QA review.");
                    }

                    state.ReleaseNotes = await File.ReadAllTextAsync(tempNotesFile, cancellationToken);
                    state.LastCompletedStep = 4;
                    SaveState(state);
                }

                // ==========================================
                // Step 5: Commit and Create PR
                // ==========================================
                if (state.LastCompletedStep < 5)
                {
                    await AnsiConsole.Status().StartAsync("Committing and creating Pull Request...", async ctx =>
                    {
                        await File.WriteAllTextAsync(tempNotesFile, state.ReleaseNotes, cancellationToken);

                        ctx.Status("Committing version changes...");
                        await ExecuteProcessAsync("git", "add .", cancellationToken);
                        string commitMsg = $"chore(release): prepare version v{state.NewVersion}";
                        await ExecuteProcessAsync("git", $"commit -m \"{commitMsg}\"", cancellationToken);

                        ctx.Status("Pushing branch to origin...");
                        await ExecuteProcessAsync("git", $"push -u origin {state.ReleaseBranch} --force", cancellationToken);

                        ctx.Status("Creating Pull Request via gh-cli...");
                        string prTitle = $"chore(release): publish version v{state.NewVersion}";
                        await ExecuteProcessAsync("gh", $"pr create --base {_settings.TargetBranch} --head {state.ReleaseBranch} --title \"{prTitle}\" --body-file \"{tempNotesFile}\"", cancellationToken);
                    });

                    state.LastCompletedStep = 5;
                    SaveState(state);
                }
            }
            catch (Exception)
            {
                // Откат только если мы упали до того, как сделали PR (до шага 5)
                if (state.LastCompletedStep < 5)
                {
                    AnsiConsole.MarkupLine("[red]Pipeline interrupted before PR creation. Rolling back local changes...[/]");
                    await ExecuteProcessAsync("git", "restore .", CancellationToken.None);
                    await ExecuteProcessAsync("git", $"checkout {state.SelectedBranch}", CancellationToken.None);
                    await ExecuteProcessAsync("git", $"branch -D {state.ReleaseBranch}", CancellationToken.None);
                    ClearState();
                }
                throw;
            }

            // ==========================================
            // Step 6: Interactive Pause
            // ==========================================
            if (state.LastCompletedStep < 6)
            {
                AnsiConsole.MarkupLine($"[yellow]Pull Request created. Please review, approve, and merge it into '{_settings.TargetBranch}' using the web interface.[/]");
                AnsiConsole.Prompt(new TextPrompt<string>("Press [green]ENTER[/] after the PR is successfully merged...").AllowEmpty());

                state.LastCompletedStep = 6;
                SaveState(state);
            }

            // ==========================================
            // Step 7: Sync and Tag
            // ==========================================
            if (state.LastCompletedStep < 7)
            {
                await AnsiConsole.Status().StartAsync("Syncing main and tagging...", async ctx =>
                {
                    ctx.Status($"Switching to {_settings.TargetBranch}...");
                    await ExecuteProcessAsync("git", $"checkout {_settings.TargetBranch}", cancellationToken);
                    ctx.Status("Pulling merge commit...");
                    await ExecuteProcessAsync("git", $"--no-pager pull origin {_settings.TargetBranch} --no-edit", cancellationToken);
                    ctx.Status($"Tagging as v{state.NewVersion}...");
                    await ExecuteProcessAsync("git", $"tag v{state.NewVersion}", cancellationToken);
                    await ExecuteProcessAsync("git", $"push origin v{state.NewVersion}", cancellationToken);
                });

                state.LastCompletedStep = 7;
                SaveState(state);
            }

            // ==========================================
            // Step 8: Build Final Artifacts
            // ==========================================
            string zipPath = Path.Combine(GetSolutionRootDirectory(), $"InstantAIGate-Server-win-x64-v{state.NewVersion}.zip");
            if (state.LastCompletedStep < 8)
            {
                bool buildDocker = AnsiConsole.Confirm("Do you want to build and push the Docker image?", defaultValue: true);

                await AnsiConsole.Status().StartAsync("Building release artifacts...", async ctx =>
                {
                    ctx.Status("Building Windows self-contained artifact...");
                    zipPath = await BuildWindowsAssetAsync(state.NewVersion, cancellationToken);

                    if (buildDocker)
                    {
                        ctx.Status("Building Linux GHCR image...");
                        await BuildDockerImageAsync(state.NewVersion, isPreRelease, cancellationToken);
                    }
                    else
                    {
                        AnsiConsole.MarkupLine("[yellow]Skipping Docker build as requested.[/]");
                    }
                });

                state.LastCompletedStep = 8;
                SaveState(state);
            }

            // ==========================================
            // Step 9: Final Publish
            // ==========================================
            if (state.LastCompletedStep < 9)
            {
                await AnsiConsole.Status().StartAsync("Publishing release...", async ctx =>
                {
                    string finalNotesFile = Path.GetTempFileName();
                    await File.WriteAllTextAsync(finalNotesFile, state.ReleaseNotes, cancellationToken);
                    string preReleaseFlag = isPreRelease ? "--prerelease" : "--latest";

                    ctx.Status("Creating GitHub Release...");
                    await ExecuteProcessAsync("gh", $"release create v{state.NewVersion} -t \"Release v{state.NewVersion}\" -F \"{finalNotesFile}\" {preReleaseFlag}", cancellationToken);

                    ctx.Status("Uploading Windows asset...");
       
                    await ExecuteProcessAsync("gh", $"release upload v{state.NewVersion} \"{zipPath}\"", cancellationToken);
                    File.Delete(finalNotesFile);


                    ctx.Status("Pushing GHCR images...");
                    await PushDockerImageAsync(state.NewVersion, isPreRelease, cancellationToken);

                    ctx.Status("Cleaning up local release branch...");
                    // We are currently on main (from Step 7), so we can safely delete the release branch
                    await ExecuteProcessAsync("git", $"branch -d {state.ReleaseBranch}", cancellationToken);

                    // ==========================================
                    // NEW: Return to original branch and sync
                    // ==========================================
                    ctx.Status($"Returning to original branch '{state.SelectedBranch}'...");
                    await ExecuteProcessAsync("git", $"checkout {state.SelectedBranch}", cancellationToken);

                    ctx.Status($"Syncing '{state.SelectedBranch}' with released version...");

                    // Use rev-list instead of cherry to detect all new commits, including merge commits
                    string diffCheck = await GetCommandOutputAsync("git", $"rev-list HEAD..{_settings.TargetBranch}", cancellationToken);

                    if (!string.IsNullOrWhiteSpace(diffCheck))
                    {
                        await ExecuteProcessAsync("git", $"--no-pager merge {_settings.TargetBranch} --no-edit", cancellationToken);

                        // Push the synchronized branch to the remote repository so GitHub recognizes the update
                        await ExecuteProcessAsync("git", $"push origin {state.SelectedBranch}", cancellationToken);
                    }
                    else
                    {
                        AnsiConsole.MarkupLine("[dim]No changes to sync. Skipping merge commit.[/]");
                    }
                });

                ClearState();
            }

            AnsiConsole.MarkupLine($"\n[bold green]Release v{state.NewVersion} published successfully![/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"\n[bold red]Pipeline Error:[/] {Markup.Escape(ex.Message)}");
            AnsiConsole.MarkupLine("[dim]Your progress has been saved. Run /release again to resume from the last successful step.[/]");
        }
    }

    private async Task ExecuteProcessLiveAsync(string fileName, string arguments, CancellationToken cancellationToken)
    {
        bool isWindows = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = isWindows ? "cmd.exe" : fileName,
                Arguments = isWindows ? $"/c {fileName} {arguments}" : arguments,
                WorkingDirectory = GetSolutionRootDirectory(),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        // Stream output in real-time
        process.OutputDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
                AnsiConsole.MarkupLine($"[grey]{Markup.Escape(e.Data)}[/]");
        };

        process.ErrorDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
                AnsiConsole.MarkupLine($"[dim red]{Markup.Escape(e.Data)}[/]");
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            throw new Exception($"Process '{fileName}' failed with exit code {process.ExitCode}.");
        }
    }
    private string GetSolutionRootDirectory()
    {
        var directory = new DirectoryInfo(Environment.CurrentDirectory);
        while (directory != null && !directory.GetFiles("InstantAIGate.sln").Any())
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? Environment.CurrentDirectory;
    }

    private void EnsureCleanWorkingDirectory()
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "status --porcelain",
                WorkingDirectory = GetSolutionRootDirectory(),
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
        string output = await GetCommandOutputAsync("git", "branch --sort=-committerdate --format=\"%(refname:short)\"", cancellationToken);

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
        if (!File.Exists(ServerProjectFile))
            throw new FileNotFoundException($"Cannot find project file at {ServerProjectFile}");

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
        string rootDir = GetSolutionRootDirectory();
        string outputDir = Path.Combine(Path.GetTempPath(), $"InstantAIGate_Build_v{version}");
        if (Directory.Exists(outputDir)) Directory.Delete(outputDir, true);

        string buildArgs = $"publish \"{ServerProjectFile}\" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o \"{outputDir}\"";
        await ExecuteProcessAsync("dotnet", buildArgs, cancellationToken);

        string nativeSourceDir = Path.Combine(rootDir, "src", "InstantAIGate.Native", "runtimes", "win-x64");
        if (Directory.Exists(nativeSourceDir))
        {
            foreach (var file in Directory.GetFiles(nativeSourceDir, "*.dll"))
            {
                File.Copy(file, Path.Combine(outputDir, Path.GetFileName(file)), true);
            }
        }

        string zipPath = Path.Combine(rootDir, $"InstantAIGate-Server-win-x64-v{version}.zip");
        if (File.Exists(zipPath)) File.Delete(zipPath);

        System.IO.Compression.ZipFile.CreateFromDirectory(outputDir, zipPath);
        return zipPath;
    }

    private async Task BuildDockerImageAsync(string newVersion, bool isPreRelease, CancellationToken cancellationToken)
    {
        string tag = $"ghcr.io/your-org/instantaigate-server:v{newVersion}";

        // Use live streaming for long-running Docker builds
        await ExecuteProcessLiveAsync("docker", $"build -f deploy/docker/Dockerfile -t {tag} .", cancellationToken);
    }

    private async Task PushDockerImageAsync(string version, bool isPreRelease, CancellationToken cancellationToken)
    {
        string imageName = "ghcr.io/instancium/instantaigate-server";
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
        bool isWindows = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = isWindows ? "cmd.exe" : fileName,
                Arguments = isWindows ? $"/c {fileName} {arguments}" : arguments,
                WorkingDirectory = GetSolutionRootDirectory(),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            string error = await process.StandardError.ReadToEndAsync(cancellationToken);
            AnsiConsole.MarkupLine($"[dim red]Process output:[/] {Markup.Escape(error)}");
        }

        return process.ExitCode;
    }

    private async Task<string> GetCommandOutputAsync(string fileName, string arguments, CancellationToken cancellationToken)
    {
        bool isWindows = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = isWindows ? "cmd.exe" : fileName,
                Arguments = isWindows ? $"/c {fileName} {arguments}" : arguments,
                WorkingDirectory = GetSolutionRootDirectory(),
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