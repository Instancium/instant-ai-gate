using System;
using System.Collections.Generic;
using System.Text;

namespace InstantAIGate.Cli.Services;

using System.Threading;
using System.Threading.Tasks;

public interface IDotnetService
{
    Task RunUnitTestsAsync(CancellationToken ct);
}
