namespace InstantAIGate.Cli.Pipeline.Steps;

using InstantAIGate.Cli.Core;
using InstantAIGate.Cli.State;
using Spectre.Console;
using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

public class BumpVersionStep : IPipelineStep
{
    private readonly string _serverProjectFile;

    public string Name => "Bump Version and Update Project File";

    public BumpVersionStep()
    {
        var dir = new DirectoryInfo(Environment.CurrentDirectory);
        while (dir != null && dir.GetFiles("InstantAIGate.sln").Length == 0) dir = dir.Parent;
        _serverProjectFile = Path.Combine(dir?.FullName ?? Environment.CurrentDirectory, "src", "InstantAIGate.Server", "InstantAIGate.Server.csproj");
    }

    public Task ExecuteAsync(PipelineContext context, CancellationToken cancellationToken)
    {
        string currentVersion = GetCurrentVersion();
        AnsiConsole.MarkupLine($"\n[dim]Current version:[/] [cyan]{currentVersion}[/]");

        var bumpType = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Select version bump strategy:")
                .AddChoices(new[] { "patch", "minor", "major", "prerelease" }));

        context.NewVersion = CalculateNewVersion(currentVersion, bumpType);
        UpdateProjectVersion(context.NewVersion);

        AnsiConsole.MarkupLine($"[green]Version successfully bumped to v{context.NewVersion}[/]");
        return Task.CompletedTask;
    }

    private string GetCurrentVersion()
    {
        if (!File.Exists(_serverProjectFile)) throw new FileNotFoundException($"Cannot find project at {_serverProjectFile}");
        var content = File.ReadAllText(_serverProjectFile);
        var match = Regex.Match(content, @"<Version>(.*?)</Version>");
        return match.Success ? match.Groups[1].Value : "1.0.0";
    }

    private void UpdateProjectVersion(string newVersion)
    {
        var content = File.ReadAllText(_serverProjectFile);
        content = Regex.Replace(content, @"<Version>.*?</Version>", $"<Version>{newVersion}</Version>");
        File.WriteAllText(_serverProjectFile, content);
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
}