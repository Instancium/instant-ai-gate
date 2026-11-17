using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Interfaces.Inference;
using InstantAIGate.Core.Interfaces.Infrastructure;

namespace InstantAIGate.Core.Services.Infrastructure;

/// <summary>
/// Resolves multimodal message parts into local file paths for the native inference layer.
///
/// SECURITY: ImageFileContent is architecturally restricted to the local plane
/// (CLI on the same machine). The remote plane rejects it in GatewayHub before
/// this resolver is ever reached. The validation here is defense-in-depth:
/// path normalization, null-byte rejection, and image-extension allow-list.
/// </summary>
public sealed class LocalTempMediaResolver : IMediaResolver
{
    private readonly IAssetManager _assetManager;

    /// <summary>
    /// Permitted file extensions for ImageFileContent on the local plane.
    /// Must cover all formats accepted by mtmd_helper_bitmap_init_from_file.
    /// </summary>
    private static readonly HashSet<string> AllowedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp", ".tiff", ".tif"
    };

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
                string safePath = ResolveSafeFilePath(fileContent.FilePath);
                resolvedPaths.Add(safePath);
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

    /// <summary>
    /// Validates and normalizes a local file path for ImageFileContent.
    /// Rejects null bytes, non-image extensions, and non-existent files.
    /// Path.GetFullPath collapses ".." segments and resolves relative paths.
    /// </summary>
    private static string ResolveSafeFilePath(string requestedPath)
    {
        if (string.IsNullOrWhiteSpace(requestedPath))
        {
            throw new ArgumentException("Media file path cannot be empty.", nameof(requestedPath));
        }

        if (requestedPath.Contains('\0'))
        {
            throw new ArgumentException("Media file path contains invalid null bytes.", nameof(requestedPath));
        }

        string fullPath = Path.GetFullPath(requestedPath);

        string extension = Path.GetExtension(fullPath);
        if (!AllowedImageExtensions.Contains(extension))
        {
            throw new UnauthorizedAccessException(
                $"File extension '{extension}' is not a permitted image type. " +
                $"Allowed: {string.Join(", ", AllowedImageExtensions)}");
        }

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Local media file not found: {fullPath}", fullPath);
        }

        return fullPath;
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