namespace InstantAIGate.Core.Services.Infrastructure;

using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Interfaces.Infrastructure;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

public sealed class LocalTempMediaResolver : IMediaResolver
{
    private readonly HttpClient _httpClient;

    public LocalTempMediaResolver(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<IMediaContext> ResolveMediaAsync(IEnumerable<MessageContent> parts, CancellationToken ct = default)
    {
        var resolvedPaths = new List<string>();
        var tempFiles = new List<string>();

        try
        {
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
                    string tempPath = Path.GetTempFileName();
                    tempFiles.Add(tempPath);

                    byte[] bytes = Convert.FromBase64String(base64Content.Base64);
                    await File.WriteAllBytesAsync(tempPath, bytes, ct);

                    resolvedPaths.Add(tempPath);
                }
                else if (part is ImageUrlContent urlContent)
                {
                    string tempPath = Path.GetTempFileName();
                    tempFiles.Add(tempPath);

                    using var response = await _httpClient.GetAsync(urlContent.Url, HttpCompletionOption.ResponseHeadersRead, ct);
                    response.EnsureSuccessStatusCode();

                    using var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
                    await response.Content.CopyToAsync(fs, ct);

                    resolvedPaths.Add(tempPath);
                }
            }

            return new TempMediaContext(resolvedPaths, tempFiles);
        }
        catch
        {
            // Clean up partial downloads on failure
            foreach (var tempFile in tempFiles)
            {
                try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { /* Ignore */ }
            }
            throw;
        }
    }

    private sealed class TempMediaContext : IMediaContext
    {
        private readonly List<string> _tempFiles;
        public IReadOnlyList<string> LocalFilePaths { get; }

        public TempMediaContext(IReadOnlyList<string> resolvedPaths, List<string> tempFiles)
        {
            LocalFilePaths = resolvedPaths;
            _tempFiles = tempFiles;
        }

        public void Dispose()
        {
            foreach (var tempFile in _tempFiles)
            {
                try
                {
                    if (File.Exists(tempFile)) File.Delete(tempFile);
                }
                catch { /* Ignore deletion errors during cleanup */ }
            }
        }
    }
}