namespace InstantAIGate.Cli.Services;

using Spectre.Console;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

public class DotnetService : IDotnetService
{
    private readonly IProcessRunner _processRunner;
    private readonly string _workingDirectory;

    public DotnetService(IProcessRunner processRunner)
    {
        _processRunner = processRunner;

        var directory = new DirectoryInfo(Environment.CurrentDirectory);
        while (directory != null && directory.GetFiles("InstantAIGate.sln").Length == 0)
        {
            directory = directory.Parent;
        }
        _workingDirectory = directory?.FullName ?? Environment.CurrentDirectory;
    }

    public async Task RunUnitTestsAsync(CancellationToken ct)
    {
        AnsiConsole.MarkupLine("[dim]Starting test discovery and execution...[/]");

        string testProject = Path.Combine(_workingDirectory, "tests", "InstantAIGate.Core.Tests", "InstantAIGate.Core.Tests.csproj");

        string arguments = $"test \"{testProject}\" -c Release --no-restore --logger \"console;verbosity=normal\"";

        int exitCode = await _processRunner.ExecuteAsync("dotnet", arguments, _workingDirectory, silent: false, ct);

        if (exitCode != 0)
        {
            throw new InvalidOperationException($"Unit tests failed with exit code {exitCode}. Pipeline halted to protect the target branch.");
        }
    }
}