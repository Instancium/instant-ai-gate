// File: src\InstantAIGate.Cli\Services\ProcessRunner.cs
namespace InstantAIGate.Cli.Services;

using Spectre.Console;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

public class ProcessRunner : IProcessRunner
{
    public async Task<int> ExecuteAsync(string fileName, string arguments, string workingDirectory, bool silent, CancellationToken ct)
    {
        bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = isWindows ? "cmd.exe" : fileName,
                Arguments = isWindows ? $"/c {fileName} {arguments}" : arguments,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        if (!silent)
        {
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
        }

        process.Start();

        if (!silent)
        {
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }

        await process.WaitForExitAsync(ct);
        return process.ExitCode;
    }

    public async Task<string> ExecuteWithOutputAsync(string fileName, string arguments, string workingDirectory, CancellationToken ct)
    {
        bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = isWindows ? "cmd.exe" : fileName,
                Arguments = isWindows ? $"/c {fileName} {arguments}" : arguments,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            }
        };

        process.Start();
        string output = await process.StandardOutput.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Command '{fileName} {arguments}' failed with exit code {process.ExitCode}.");
        }

        return output;
    }
}