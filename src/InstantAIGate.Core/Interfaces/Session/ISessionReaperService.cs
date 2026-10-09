using Microsoft.Extensions.Hosting;
using System.Threading;
using System.Threading.Tasks;

namespace InstantAIGate.Core.Interfaces.Session;

/// <summary>
/// Defines a background service responsible for periodically reaping orphaned ephemeral sessions
/// that may have survived a hard process crash or missed disconnection events.
/// </summary>
public interface ISessionReaperService : IHostedService
{
    /// <summary>
    /// Executes a single cleanup pass to identify and destroy orphaned ephemeral sessions.
    /// </summary>
    /// <param name="cancellationToken">Token to observe for cancellation requests.</param>
    /// <returns>A task representing the asynchronous cleanup operation.</returns>
    Task CleanupOrphanedSessionsAsync(CancellationToken cancellationToken);
}