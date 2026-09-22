using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Interfaces.Inference;
using InstantAIGate.Core.Interfaces.Infrastructure;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace InstantAIGate.Core.Services.Infrastructure;

public sealed class LocalTempMediaResolver : IMediaResolver
{
    private readonly IAssetManager _assetManager;

    public LocalTempMediaResolver(IAssetManager assetManager)
    {
        _assetManager = assetManager ?? throw new ArgumentNullException(nameof(assetManager));
    }

    public async Task<IMediaContext> ResolveMediaAsync(IEnumerable<MessageContent> parts, CancellationToken ct = default)
    {
        var resolvedPaths = new List<string>();

        foreach (var part in parts)
        {
            if (part is ImageFileContent fileContent)
            {
                if (!File.Exists(fileContent.FilePath))
                    throw new FileNotFoundException($"Local media file not found: {fileContent.FilePath}");

                resolvedPaths.Add(fileContent.FilePath);
            }
            else if (part is ImageBase64Content base64Content)
            {
                string path = await _assetManager.GetOrCacheMediaAsync(base64Content.Base64, ct);
                resolvedPaths.Add(path);
            }
            else if (part is ImageUrlContent urlContent)
            {
                string path = await _assetManager.GetOrCacheMediaAsync(urlContent.Url, ct);
                resolvedPaths.Add(path);
            }
        }

        return new CachedMediaContext(resolvedPaths);
    }

    private sealed class CachedMediaContext : IMediaContext
    {
        public IReadOnlyList<string> LocalFilePaths { get; }

        public CachedMediaContext(IReadOnlyList<string> resolvedPaths)
        {
            LocalFilePaths = resolvedPaths;
        }

        public void Dispose()
        {
            // No-op: The lifecycle of these files is now managed globally by MemoryAssetManager.
        }
    }
}