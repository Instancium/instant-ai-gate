namespace InstantAIGate.SSR.Downloader;

using InstantAIGate.Core.Exceptions;
using InstantAIGate.Core.Interfaces.Inference;
using InstantAIGate.SSR.Contracts;
using InstantAIGate.SSR.Dtos;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

public class ParallelModelDownloader : IModelDownloader, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ParallelModelDownloader> _logger;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _activeDownloads;

    private const int BufferSize = 81920; // 80 KB
    private const int MaxDegreesOfParallelism = 4;
    private const long MinimumParallelSize = 10 * 1024 * 1024; // 10 MB
    private readonly IModelValidator _modelValidator;
    private record FileDownloadContext(string Url, string DestinationPath, string TempPath, long TotalBytes, bool AcceptRanges);
    public ParallelModelDownloader(HttpClient httpClient, ILogger<ParallelModelDownloader> logger, IModelValidator modelValidator)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _activeDownloads = new ConcurrentDictionary<string, CancellationTokenSource>();
        _modelValidator = modelValidator ?? throw new ArgumentNullException();
    }

    public async Task DownloadModelAsync(
            string modelId,
            IReadOnlyList<string> downloadUrls,
            string destinationDirectory,
            IProgress<DownloadProgress> progress,
            CancellationToken ct = default)
    {
        if (downloadUrls == null || !downloadUrls.Any())
            throw new ArgumentException("No download URLs provided.", nameof(downloadUrls));

        Directory.CreateDirectory(destinationDirectory);

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (!_activeDownloads.TryAdd(modelId, linkedCts))
        {
            throw new InvalidOperationException($"Download for {modelId} is already in progress.");
        }

        try
        {
            long totalBytesAllFiles = 0;
            var fileTasks = new List<FileDownloadContext>();

            foreach (var url in downloadUrls)
            {
                var uri = new Uri(url);
                string fileName = Path.GetFileName(uri.LocalPath);
                if (string.IsNullOrEmpty(fileName)) fileName = Guid.NewGuid().ToString("N") + ".bin";

                string destPath = Path.Combine(destinationDirectory, fileName);
                string tempPath = destPath + ".tmp"; 

                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linkedCts.Token);
                response.EnsureSuccessStatusCode();

                long size = response.Content.Headers.ContentLength ?? 0;
                bool acceptRanges = response.Headers.AcceptRanges.Contains("bytes");
                totalBytesAllFiles += size;

                fileTasks.Add(new FileDownloadContext(url, destPath, tempPath, size, acceptRanges));
            }

            long totalDownloadedBytes = 0;
            var sw = Stopwatch.StartNew();
            var progressLock = new object();
            bool isDownloadCompletedAndValid = false;

            try
            {
                foreach (var file in fileTasks)
                {
        
                    if (file.TotalBytes > MinimumParallelSize && file.AcceptRanges)
                    {
                        await DownloadParallelAsync(modelId, file.Url, file.TempPath, file.TotalBytes,
                            bytesRead => ReportProgress(bytesRead), linkedCts.Token);
                    }
                    else
                    {
                        await DownloadSequentialAsync(modelId, file.Url, file.TempPath, file.TotalBytes,
                            bytesRead => ReportProgress(bytesRead), linkedCts.Token);
                    }
                }

                foreach (var file in fileTasks)
                {
                    var fileInfo = new FileInfo(file.TempPath);
                    if (fileInfo.Exists && fileInfo.Length < 1024 * 1024)
                    {
                        _logger.LogError("Downloaded file '{File}' is abnormally small.", fileInfo.Name);
                        throw new InvalidOperationException("Download failed: The remote server returned an invalid file.");
                    }
                }


                var downloadedFiles = fileTasks.Select(f => f.TempPath).ToList();
                bool isValid = await _modelValidator.ValidateIntegrityAsync(downloadedFiles, linkedCts.Token);

                if (!isValid)
                {
                    _logger.LogError("GGUF headers validation failed for model {ModelId}.", modelId);
                    throw new ModelIntegrityException(modelId, downloadedFiles.First());
                }

 
                foreach (var file in fileTasks)
                {
                    File.Move(file.TempPath, file.DestinationPath, overwrite: true);
                }

                isDownloadCompletedAndValid = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Download process failed or validation was rejected for model {ModelId}.", modelId);
                throw;
            }
            finally
            {
                if (!isDownloadCompletedAndValid)
                {
                    foreach (var file in fileTasks)
                    {
                        if (File.Exists(file.TempPath))
                        {
                            File.Delete(file.TempPath);
                        }
                    }
                }
            }

            void ReportProgress(int bytesRead)
            {
                lock (progressLock)
                {
                    totalDownloadedBytes += bytesRead;
                    double speed = totalDownloadedBytes / sw.Elapsed.TotalSeconds;
                    float percent = totalBytesAllFiles > 0 ? (float)totalDownloadedBytes / totalBytesAllFiles * 100 : 0f;
                    if (percent >= 100f && totalDownloadedBytes < totalBytesAllFiles) percent = 99.9f;
                    progress.Report(new DownloadProgress(modelId, totalDownloadedBytes, totalBytesAllFiles, speed, percent));
                }
            }
        }
        finally
        {
            _activeDownloads.TryRemove(modelId, out _);
        }
    }

    private async Task DownloadParallelAsync(string modelId, string url, string destination, long totalBytes, Action<int> onBytesRead, CancellationToken ct)
    {
        long chunkSize = totalBytes / MaxDegreesOfParallelism;
        using var fs = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.Write, BufferSize, useAsync: true);
        fs.SetLength(totalBytes);

        var tasks = new Task[MaxDegreesOfParallelism];
        for (int i = 0; i < MaxDegreesOfParallelism; i++)
        {
            long start = i * chunkSize;
            long end = (i == MaxDegreesOfParallelism - 1) ? totalBytes - 1 : (start + chunkSize - 1);

            tasks[i] = Task.Run(async () =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Range = new RangeHeaderValue(start, end);

                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode(); 

                using var stream = await response.Content.ReadAsStreamAsync(ct);
                byte[] buffer = new byte[BufferSize];
                int read;
                long localPosition = start;

                using var threadFs = new FileStream(destination, FileMode.Open, FileAccess.Write, FileShare.Write, BufferSize, useAsync: true);
                threadFs.Seek(localPosition, SeekOrigin.Begin);

                while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
                {
                    await threadFs.WriteAsync(buffer, 0, read, ct);
                    onBytesRead(read);
                }
            }, ct);
        }
        await Task.WhenAll(tasks);
    }

    private async Task DownloadSequentialAsync(string modelId, string url, string destination, long totalBytes, Action<int> onBytesRead, CancellationToken ct)
    {
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode(); 

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var fs = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);
        byte[] buffer = new byte[BufferSize];
        int read;

        while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
        {
            await fs.WriteAsync(buffer, 0, read, ct);
            onBytesRead(read);
        }
    }

    public Task CancelDownloadAsync(string modelId)
    {
        if (_activeDownloads.TryGetValue(modelId, out var cts))
        {
            cts.Cancel();
        }
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        foreach (var cts in _activeDownloads.Values)
        {
            cts.Cancel();
            cts.Dispose();
        }
        _activeDownloads.Clear();
    }

}