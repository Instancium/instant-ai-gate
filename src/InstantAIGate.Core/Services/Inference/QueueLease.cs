namespace InstantAIGate.Core.Services.Inference;

using InstantAIGate.Core.Interfaces.Inference;
using System;

internal sealed class QueueLease : IQueueLease
{
    private readonly DynamicRequestQueue _queue;
    private bool _disposed;

    public string TenantId { get; }
    public TimeSpan WaitDuration { get; }

    public QueueLease(DynamicRequestQueue queue, string tenantId, TimeSpan waitDuration)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        TenantId = tenantId;
        WaitDuration = waitDuration;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _queue.Release();
            _disposed = true;
        }
    }
}