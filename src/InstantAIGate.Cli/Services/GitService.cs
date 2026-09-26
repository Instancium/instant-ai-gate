namespace InstantAIGate.Cli.Services;

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

public class GitService : IGitService
{
    private readonly IProcessRunner _processRunner;
    private readonly string _workingDirectory;

    public GitService(IProcessRunner processRunner)
    {
        _processRunner = processRunner;
        _workingDirectory = GetSolutionRootDirectory();
    }

    public async Task AddAllAsync(CancellationToken ct) =>
        await ExecuteCommandAsync("git", "add .", ct);

    public async Task<string> GetCachedDiffAsync(CancellationToken ct) =>
        await _processRunner.ExecuteWithOutputAsync("git", "diff --cached", _workingDirectory, ct);

    public async Task CommitAsync(string message, CancellationToken ct)
    {
        string tempFilePath = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFilePath, message, ct);
            await ExecuteCommandAsync("git", $"commit -F \"{tempFilePath}\"", ct);
        }
        finally
        {
            if (File.Exists(tempFilePath)) File.Delete(tempFilePath);
        }
    }

    public async Task PushBranchAsync(string branchName, CancellationToken ct) =>
        await ExecuteCommandAsync("git", $"push origin {branchName}", ct);

    public async Task PushCurrentBranchAsync(CancellationToken ct)
    {
        string branch = await GetCurrentBranchAsync(ct);
        await PushBranchAsync(branch, ct);
    }

    public async Task<string> GetCurrentBranchAsync(CancellationToken ct) =>
        (await _processRunner.ExecuteWithOutputAsync("git", "branch --show-current", _workingDirectory, ct)).Trim();

    public async Task CheckoutAsync(string branchName, CancellationToken ct) =>
        await ExecuteCommandAsync("git", $"checkout {branchName}", ct);

    public async Task PullAsync(CancellationToken ct) =>
        await ExecuteCommandAsync("git", "pull origin", ct);

    public async Task MergeNoFastForwardAsync(string sourceBranch, string message, CancellationToken ct)
    {
 
        string diffCheck = await _processRunner.ExecuteWithOutputAsync("git", $"diff HEAD..{sourceBranch}", _workingDirectory, ct);
        if (string.IsNullOrWhiteSpace(diffCheck))
        {
            return;
        }

        string tempFilePath = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFilePath, message, ct);
            await ExecuteCommandAsync("git", $"merge {sourceBranch} --no-ff -F \"{tempFilePath}\"", ct);
        }
        finally
        {
            if (File.Exists(tempFilePath)) File.Delete(tempFilePath);
        }
    }

    public async Task CreateAndPushTagAsync(string tagName, CancellationToken ct)
    {
        await ExecuteCommandAsync("git", $"tag {tagName}", ct);
        await ExecuteCommandAsync("git", $"push origin {tagName}", ct);
    }

    public async Task DeleteLocalBranchAsync(string branchName, CancellationToken ct) =>
        await ExecuteCommandAsync("git", $"branch -d {branchName}", ct);

    public async Task<string> GetLatestTagAsync(CancellationToken ct) =>
        (await _processRunner.ExecuteWithOutputAsync("git", "describe --tags --abbrev=0", _workingDirectory, ct)).Trim();

    public async Task<string> GetGitLogAsync(string fromTag, CancellationToken ct)
    {
        string arguments = string.IsNullOrWhiteSpace(fromTag)
            ? "log -n 20 --oneline"
            : $"log {fromTag}..HEAD --oneline";

        return await _processRunner.ExecuteWithOutputAsync("git", arguments, _workingDirectory, ct);
    }

    private async Task ExecuteCommandAsync(string fileName, string arguments, CancellationToken ct)
    {
        int exitCode = await _processRunner.ExecuteAsync(fileName, arguments, _workingDirectory, silent: false, ct);
        if (exitCode != 0)
        {
            throw new InvalidOperationException($"Git command failed with exit code {exitCode}.");
        }
    }

    private string GetSolutionRootDirectory()
    {
        var directory = new DirectoryInfo(Environment.CurrentDirectory);
        while (directory != null && directory.GetFiles("InstantAIGate.sln").Length == 0)
        {
            directory = directory.Parent;
        }

        if (directory == null)
        {
            throw new DirectoryNotFoundException("Could not locate InstantAIGate.sln in the current directory tree.");
        }

        return directory.FullName;
    }
}