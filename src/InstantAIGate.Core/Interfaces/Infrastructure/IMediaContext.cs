namespace InstantAIGate.Core.Interfaces.Infrastructure;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public interface IMediaContext : IDisposable
{
    IReadOnlyList<string> LocalFilePaths { get; }
}

public interface IMediaResolver
{
    Task<IMediaContext> ResolveMediaAsync(IEnumerable<Dtos.Inference.MessageContent> parts, CancellationToken ct = default);
}