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
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

public class ParallelModelDownloader : IModelDownloader, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ParallelModelDownloader> _logger;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _activeDownloads;
    private const int BufferSize = 81920;
    private const int MaxDegreesOfParallelism = 4;
    private const long MinimumParallelSize = 10 * 1024 * 1024;
    private readonly IModelValidator _modelValidator;

    private record FileDownloadContext(string Url, string DestinationPath, string TempPath, string ManifestPath, long TotalBytes, bool AcceptRanges);

    public class DownloadManifest
    {
        public long TotalBytes { get; set; }
        public long[] ChunkProgress { get; set; } = new long[MaxDegreesOfParallelism];
        public bool IsSequential { get; set; }
    }

    public ParallelModelDownloader(HttpClient httpClient, ILogger<ParallelModelDownloader> logger, IModelValidator modelValidator)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _activeDownloads = new ConcurrentDictionary<string, CancellationTokenSource>();
        _modelValidator = modelValidator ?? throw new ArgumentNullException();
    }

    public async Task DownloadModelAsync(
        string modelId, IReadOnlyList<string> downloadUrls, string destinationDirectory, IProgress<DownloadProgress> progress, CancellationToken ct = default)
    {
        if (downloadUrls == null || !downloadUrls.Any())
            throw new ArgumentException("No download URLs provided.", nameof(downloadUrls));

        Directory.CreateDirectory(destinationDirectory);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        if (!_activeDownloads.TryAdd(modelId, linkedCts))
            throw new InvalidOperationException($"Download for {modelId} is already in progress.");

        try
        {
            long totalBytesAllFiles = 0;
            var fileTasks = new List<FileDownloadContext>();

            // Phase 1: Headers Inspection
            foreach (var url in downloadUrls)
            {
                var uri = new Uri(url);
                string fileName = Path.GetFileName(uri.LocalPath);
                if (string.IsNullOrEmpty(fileName)) fileName = Guid.NewGuid().ToString("N") + ".bin";
                string destPath = Path.Combine(destinationDirectory, fileName);
                string tempPath = destPath + ".tmp";
                string manifestPath = tempPath + ".meta.json";

                long size = 0;
                bool acceptRanges = false;


                using var rangeRequest = new HttpRequestMessage(HttpMethod.Get, url);
                rangeRequest.Headers.Range = new RangeHeaderValue(0, 0);
                using var rangeResponse = await _httpClient.SendAsync(rangeRequest, HttpCompletionOption.ResponseHeadersRead, linkedCts.Token);

                if (rangeResponse.StatusCode == System.Net.HttpStatusCode.PartialContent)
                {
                    size = rangeResponse.Content.Headers.ContentRange?.Length ?? 0;
                    acceptRanges = true;
                }
                else if (rangeResponse.IsSuccessStatusCode)
                {

                    size = rangeResponse.Content.Headers.ContentLength ?? 0;
                }
                else
                {
                    rangeResponse.EnsureSuccessStatusCode();
                }

                totalBytesAllFiles += size;
                fileTasks.Add(new FileDownloadContext(url, destPath, tempPath, manifestPath, size, acceptRanges));
            }

            long totalDownloadedBytes = 0;
            long sessionDownloadedBytes = 0;
            var sw = Stopwatch.StartNew();
            var uiThrottleSw = Stopwatch.StartNew();
            var logThrottleSw = Stopwatch.StartNew();
            var progressLock = new object();

            try
            {
                // Phase 2: Stateful Download
                foreach (var file in fileTasks)
                {
                    DownloadManifest manifest = new DownloadManifest { TotalBytes = file.TotalBytes, IsSequential = !file.AcceptRanges };

                    if (File.Exists(file.ManifestPath) && File.Exists(file.TempPath))
                    {
                        try
                        {
                            var json = await File.ReadAllTextAsync(file.ManifestPath, linkedCts.Token);
                            var existingManifest = JsonSerializer.Deserialize<DownloadManifest>(json);
                            if (existingManifest != null && existingManifest.TotalBytes == file.TotalBytes && file.AcceptRanges)
                            {
                                manifest = existingManifest;
                                _logger.LogInformation("Resuming download for {File}. Existing progress found.", Path.GetFileName(file.DestinationPath));
                            }
                            else
                            {
                                if (File.Exists(file.TempPath)) File.Delete(file.TempPath);
                            }
                        }
                        catch
                        {
                            if (File.Exists(file.TempPath)) File.Delete(file.TempPath);
                        }
                    }

                    long fileAlreadyDownloaded = manifest.ChunkProgress.Sum();
                    totalDownloadedBytes += fileAlreadyDownloaded;

                    if (file.TotalBytes > MinimumParallelSize && file.AcceptRanges)
                    {
                        await DownloadParallelAsync(file.Url, file.TempPath, file.TotalBytes, manifest.ChunkProgress,
                            (chunkIndex, bytesRead) => ReportProgress(file, manifest, chunkIndex, bytesRead), linkedCts.Token);
                    }
                    else
                    {
                        manifest.ChunkProgress[0] = 0; // Reset for sequential
                        await DownloadSequentialAsync(file.Url, file.TempPath,
                            (bytesRead) => ReportProgress(file, manifest, 0, bytesRead), linkedCts.Token);
                    }
                }

                // Phase 3: Integrity Validation
                var downloadedFiles = fileTasks.Select(f => f.TempPath).ToList();
                bool isValid = await _modelValidator.ValidateIntegrityAsync(downloadedFiles, linkedCts.Token);

                if (!isValid)
                {
                    _logger.LogError("GGUF headers validation failed for model {ModelId}. Temporary files will be wiped.", modelId);
                    foreach (var file in fileTasks)
                    {
                        if (File.Exists(file.TempPath)) File.Delete(file.TempPath);
                        if (File.Exists(file.ManifestPath)) File.Delete(file.ManifestPath);
                    }
                    throw new ModelIntegrityException(modelId, downloadedFiles.First());
                }

                // Commit files
                foreach (var file in fileTasks)
                {
                    if (File.Exists(file.DestinationPath)) File.Delete(file.DestinationPath);
                    File.Move(file.TempPath, file.DestinationPath);
                    if (File.Exists(file.ManifestPath)) File.Delete(file.ManifestPath);
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException || ex is ObjectDisposedException || ex.InnerException is ObjectDisposedException)
            {
                _logger.LogInformation("Download process for model {ModelId} was cancelled. Progress saved to manifest.", modelId);
                throw new OperationCanceledException("Download aborted.", ex);
            }
            catch (ModelIntegrityException)
            {
                throw; // Already cleaned up
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Network or IO error during download for model {ModelId}. Progress saved.", modelId);
                throw;
            }

            void ReportProgress(FileDownloadContext file, DownloadManifest manifest, int chunkIndex, int bytesRead)
            {
                lock (progressLock)
                {
                    totalDownloadedBytes += bytesRead;
                    sessionDownloadedBytes += bytesRead;
                    manifest.ChunkProgress[chunkIndex] += bytesRead;

                    bool logTriggered = logThrottleSw.ElapsedMilliseconds > 3000;
                    bool uiTriggered = uiThrottleSw.ElapsedMilliseconds > 500;
                    bool isComplete = totalBytesAllFiles > 0 && totalDownloadedBytes >= totalBytesAllFiles;

                    double speed = sessionDownloadedBytes / sw.Elapsed.TotalSeconds;
                    float percent = totalBytesAllFiles > 0 ? (float)totalDownloadedBytes / totalBytesAllFiles * 100 : 0f;
                    if (percent >= 100f && totalDownloadedBytes < totalBytesAllFiles) percent = 99.9f;

                    if (logTriggered || isComplete)
                    {
                        File.WriteAllText(file.ManifestPath, JsonSerializer.Serialize(manifest));
                        logThrottleSw.Restart();

                        _logger.LogInformation("[Download: {ModelId}] {Percent:F1}% | {DownloadedMB:F2}/{TotalMB:F2} MB | {Speed:F2} MB/s",
                            modelId, percent, totalDownloadedBytes / 1048576.0, totalBytesAllFiles / 1048576.0, speed / 1048576.0);
                    }

                    if (uiTriggered || isComplete)
                    {
                        uiThrottleSw.Restart();
                        progress.Report(new DownloadProgress(modelId, totalDownloadedBytes, totalBytesAllFiles, speed, percent));
                    }
                }
            }
        }
        finally
        {
            _activeDownloads.TryRemove(modelId, out _);
        }
    }

    private async Task DownloadParallelAsync(string url, string destination, long totalBytes, long[] chunkProgress, Action<int, int> onBytesRead, CancellationToken ct)
    {
        long chunkSize = totalBytes / MaxDegreesOfParallelism;

        using var fs = new FileStream(destination, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Write, BufferSize, useAsync: true);
        if (fs.Length != totalBytes)
        {
            fs.SetLength(totalBytes);
        }

        var tasks = new Task[MaxDegreesOfParallelism];
        for (int i = 0; i < MaxDegreesOfParallelism; i++)
        {
            int chunkIndex = i;
            long start = chunkIndex * chunkSize;
            long end = (chunkIndex == MaxDegreesOfParallelism - 1) ? totalBytes - 1 : (start + chunkSize - 1);
            long alreadyDownloaded = chunkProgress[chunkIndex];

            if (alreadyDownloaded >= (end - start + 1)) continue;

            long currentStart = start + alreadyDownloaded;

            tasks[chunkIndex] = Task.Run(async () =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Range = new RangeHeaderValue(currentStart, end);
                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();

                using var stream = await response.Content.ReadAsStreamAsync(ct);
                byte[] buffer = new byte[BufferSize];
                int read;

                using var threadFs = new FileStream(destination, FileMode.Open, FileAccess.Write, FileShare.Write, BufferSize, useAsync: true);
                threadFs.Seek(currentStart, SeekOrigin.Begin);

                while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
                {
                    await threadFs.WriteAsync(buffer, 0, read, ct);
                    onBytesRead(chunkIndex, read);
                }
            }, ct);
        }

        await Task.WhenAll(tasks.Where(t => t != null));
    }

    private async Task DownloadSequentialAsync(string url, string destination, Action<int> onBytesRead, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
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