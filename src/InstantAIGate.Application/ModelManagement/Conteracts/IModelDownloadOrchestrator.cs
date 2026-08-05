using System;
using System.Collections.Generic;
using System.Text;

namespace InstantAIGate.Application.ModelManagement.Conteracts
{
    public interface IModelDownloadOrchestrator
    {
        IAsyncEnumerable<AggregateDownloadProgress> ExecuteDownloadAsync(string repoId, CancellationToken cancellationToken);
    }
}
