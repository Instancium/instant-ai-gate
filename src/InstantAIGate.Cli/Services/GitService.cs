namespace InstantAIGate.Cli.Services;

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

public class GitService : IGitService
{
    private readonly string _workingDirectory;

    public GitService()
    {
        _workingDirectory = GetSolutionRootDirectory();
    }

    public async Task AddAllAsync(CancellationToken ct)
    {
        await ExecuteCommandAsync("git", "add .", ct);
    }

    public async Task<string> GetCachedDiffAsync(CancellationToken ct)
    {
        return await ExecuteCommandWithOutputAsync("git", "diff --cached", ct);
    }

    public async Task CommitAsync(string message, CancellationToken ct)
    {
        string escapedMessage = message.Replace("\"", "\\\"");
        await ExecuteCommandAsync("git", $"commit -m \"{escapedMessage}\"", ct);
    }

    public async Task PushCurrentBranchAsync(CancellationToken ct)
    {
        string branch = (await ExecuteCommandWithOutputAsync("git", "branch --show-current", ct)).Trim();
        if (string.IsNullOrWhiteSpace(branch))
        {
            throw new InvalidOperationException("Could not determine the current git branch.");
        }

        await ExecuteCommandAsync("git", $"push origin {branch}", ct);
    }

    private async Task ExecuteCommandAsync(string fileName, string arguments, CancellationToken ct)
    {
        int exitCode = await RunProcessAsync(fileName, arguments, false, ct);
        if (exitCode != 0)
        {
            throw new InvalidOperationException($"Command '{fileName} {arguments}' failed with exit code {exitCode}.");
        }
    }

    private async Task<string> ExecuteCommandWithOutputAsync(string fileName, string arguments, CancellationToken ct)
    {
        bool isWindows = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = isWindows ? "cmd.exe" : fileName,
                Arguments = isWindows ? $"/c {fileName} {arguments}" : arguments,
                WorkingDirectory = _workingDirectory,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        string output = await process.StandardOutput.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        return output;
    }

    private async Task<int> RunProcessAsync(string fileName, string arguments, bool redirectOutput, CancellationToken ct)
    {
        bool isWindows = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = isWindows ? "cmd.exe" : fileName,
                Arguments = isWindows ? $"/c {fileName} {arguments}" : arguments,
                WorkingDirectory = _workingDirectory,
                RedirectStandardOutput = redirectOutput,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        await process.WaitForExitAsync(ct);
        return process.ExitCode;
    }

    private string GetSolutionRootDirectory()
    {
        var directory = new DirectoryInfo(Environment.CurrentDirectory);
        while (directory != null && directory.GetFiles("InstantAIGate.sln").Length == 0)
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? Environment.CurrentDirectory;
    }
}