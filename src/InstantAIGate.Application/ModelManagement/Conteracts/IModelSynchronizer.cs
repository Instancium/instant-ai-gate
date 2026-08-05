using InstantAIGate.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace InstantAIGate.Application.ModelManagement.Conteracts
{
    public interface IModelSynchronizer
    {
        IAsyncEnumerable<AggregateDownloadProgress> SynchronizeModelAsync(SupportedModelDefinition definition, CancellationToken cancellationToken);
    }
}
