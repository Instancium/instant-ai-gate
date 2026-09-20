namespace InstantAIGate.Core.Interfaces.Inference;

using System;
using System.Threading;
using System.Threading.Tasks;

public interface IQueueManager
{
    int MaxQueueSize { get; }
    int PendingCount { get; }

    void UpdateQueueLimit(int newLimit);
    void Pause();
    void Resume();

    Task<IQueueLease> EnqueueRequestAsync(string tenantId, TimeSpan timeout, CancellationToken ct = default);
}