namespace InstantAIGate.Core.Interfaces.Inference;

using System;

public interface IQueueLease : IDisposable
{
    string TenantId { get; }
    TimeSpan WaitDuration { get; }
}