using System;
using System.Collections.Generic;
using System.Text;

namespace InstantAIGate.Application.Adapters
{
    public interface IGenAiAdapter : IDisposable
    {
        void Initialize(string modelDirectory, string executionProvider);

        IAsyncEnumerable<string> GenerateStreamAsync(
            string prompt,
            IReadOnlyList<string> imagePaths,
            IReadOnlyList<string> audioPaths,
            int maxLength,
            CancellationToken cancellationToken = default);
    }
}
