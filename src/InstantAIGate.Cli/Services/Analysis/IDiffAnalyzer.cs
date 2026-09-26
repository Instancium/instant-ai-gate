using System;
using System.Collections.Generic;
using System.Text;

namespace InstantAIGate.Cli.Services.Analysis
{
    using System.Threading;
    using System.Threading.Tasks;

    public interface IDiffAnalyzer
    {
        Task<string> AnalyzeAndSummarizeAsync(string rawDiff, CancellationToken ct);
    }
}
