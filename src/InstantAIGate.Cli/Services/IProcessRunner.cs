using System;
using System.Collections.Generic;
using System.Text;

namespace InstantAIGate.Cli.Services;

using System.Threading;
using System.Threading.Tasks;

public interface IProcessRunner
{
    Task<int> ExecuteAsync(string fileName, string arguments, string workingDirectory, bool silent, CancellationToken ct);
    Task<string> ExecuteWithOutputAsync(string fileName, string arguments, string workingDirectory, CancellationToken ct);
}
