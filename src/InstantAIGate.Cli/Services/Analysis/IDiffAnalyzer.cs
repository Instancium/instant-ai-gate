namespace InstantAIGate.Cli.Services.Analysis;

using System.Threading;
using System.Threading.Tasks;

public interface IDiffAnalyzer
{
    Task<string> AnalyzeAndSummarizeAsync(string rawDiff, CancellationToken ct);
    Task<string> AnalyzeAndSummarizeAsync(string rawDiff, string diffStat, CancellationToken ct);
}