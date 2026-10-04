namespace InstantAIGate.Core.Tests.Downloader;

using InstantAIGate.Core.Tests.Inference.Stubs;
using InstantAIGate.Core.Tests.Infrastructure;
using InstantAIGate.Core.Tests.TestConfiguration;
using InstantAIGate.SSR.Downloader;
using InstantAIGate.SSR.Dtos;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

public class ParallelModelDownloaderTests : IDisposable
{
    // Synthetic sizes and URLs come from the test project appsettings.json.
    private static readonly TestSyntheticDownloadOptions SynthOptions = TestConfig.Model.SyntheticDownload;

    private readonly string _tempTestDir;

    public ParallelModelDownloaderTests()
    {
        // Ensure clean I/O environment for each test
        _tempTestDir = Path.Combine(Path.GetTempPath(), "InstantAIGate_Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempTestDir);
    }


    [Fact]
    public async Task DownloadModelAsync_Resume_ContinuesFromManifest()
    {
        // Setup
        var stubValidator = new StubModelValidator { ShouldPass = true };
        var handler = new SyntheticNetworkHandler(virtualFileSizeBytes: SynthOptions.ParallelDownloadFileSizeBytes, supportRanges: true);
        var downloader = new ParallelModelDownloader(new HttpClient(handler), NullLogger<ParallelModelDownloader>.Instance, stubValidator);

        string modelId = "resume-test";
        string url = SynthOptions.BaseUrl + "resume.gguf";
        string destPath = Path.Combine(_tempTestDir, "resume.gguf");
        string tempPath = destPath + ".tmp";
        string manifestPath = tempPath + ".meta.json";

       
        long totalSize = SynthOptions.ParallelDownloadFileSizeBytes;
        long chunkSize = totalSize / 4; // MaxDegreesOfParallelism = 4
        long preDownloaded = chunkSize / 2;

        var manifest = new ParallelModelDownloader.DownloadManifest { TotalBytes = totalSize };
        manifest.ChunkProgress[0] = preDownloaded;

        using (var fs = new FileStream(tempPath, FileMode.Create))
        {
            fs.SetLength(totalSize);
        }
        File.WriteAllText(manifestPath, System.Text.Json.JsonSerializer.Serialize(manifest));

        var progressList = new List<DownloadProgress>();
        var progress = new Progress<DownloadProgress>(progressList.Add);

        // Act
        await downloader.DownloadModelAsync(modelId, new[] { url }, _tempTestDir, progress);

        // Assert
        Assert.True(File.Exists(destPath));
        Assert.False(File.Exists(tempPath));      
        Assert.False(File.Exists(manifestPath));  
        Assert.Equal(totalSize, new FileInfo(destPath).Length);

  
        Assert.NotEmpty(progressList);
        var finalProgress = progressList.Last();
        Assert.Equal(100f, finalProgress.Percentage);
        Assert.Equal(totalSize, finalProgress.BytesDownloaded);
    }

    [Fact]
    public async Task DownloadModelAsync_MultiThreaded_CalculatesSpeedAndCompletes()
    {
        // Arrange
        var stubValidator = new StubModelValidator { ShouldPass = true };
        var handler = new SyntheticNetworkHandler(virtualFileSizeBytes: SynthOptions.ParallelDownloadFileSizeBytes, supportRanges: true);
        var downloader = new ParallelModelDownloader(
            new HttpClient(handler),
            NullLogger<ParallelModelDownloader>.Instance,
            stubValidator);
        var progressList = new List<DownloadProgress>();
        var progress = new Progress<DownloadProgress>(progressList.Add);

        // Act
        await downloader.DownloadModelAsync("test-model", new[] { SynthOptions.BaseUrl + "model.gguf" }, _tempTestDir, progress);

        // Assert
        Assert.NotEmpty(progressList);
        var finalProgress = progressList.Last();
        Assert.Equal(100f, finalProgress.Percentage);
        Assert.Equal(finalProgress.TotalBytes, finalProgress.BytesDownloaded);
        Assert.True(finalProgress.SpeedBytesPerSecond > 0);

        string expectedFile = Path.Combine(_tempTestDir, "model.gguf");
        Assert.True(File.Exists(expectedFile));
        Assert.Equal(SynthOptions.ParallelDownloadFileSizeBytes, new FileInfo(expectedFile).Length);
    }

    [Fact]
    public async Task DownloadModelAsync_WithoutRangeSupport_FallsBackToSequential()
    {
        // Arrange: server explicitly rejects Range requests
        var stubValidator = new StubModelValidator { ShouldPass = true };
        var handler = new SyntheticNetworkHandler(virtualFileSizeBytes: SynthOptions.SequentialFallbackFileSizeBytes, supportRanges: false);
        var downloader = new ParallelModelDownloader(new HttpClient(handler),
            NullLogger<ParallelModelDownloader>.Instance, stubValidator);
        var progressList = new List<DownloadProgress>();
        var progress = new Progress<DownloadProgress>(progressList.Add);

        // Act
        await downloader.DownloadModelAsync("fallback-model", new[] { SynthOptions.BaseUrl + "sequential.gguf" }, _tempTestDir, progress);

        // Assert
        Assert.NotEmpty(progressList);
        Assert.Equal(100f, progressList.Last().Percentage);

        string expectedFile = Path.Combine(_tempTestDir, "sequential.gguf");
        Assert.True(File.Exists(expectedFile));
        Assert.Equal(SynthOptions.SequentialFallbackFileSizeBytes, new FileInfo(expectedFile).Length);
    }

    [Fact]
    public async Task DownloadModelAsync_WhenCancelled_StopsAndReleasesFileLocks()
    {
        // Arrange: massive virtual file to ensure it doesn't finish before cancellation
        var stubValidator = new StubModelValidator { ShouldPass = true };
        var handler = new SyntheticNetworkHandler(virtualFileSizeBytes: SynthOptions.CancellationTestFileSizeBytes);
        var downloader = new ParallelModelDownloader(new HttpClient(handler),
            NullLogger<ParallelModelDownloader>.Instance, stubValidator);

        using var cts = new CancellationTokenSource();
        var progress = new Progress<DownloadProgress>(p =>
        {
            // Cancel as soon as we start receiving data
            if (p.Percentage > 1.0f) cts.Cancel();
        });

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await downloader.DownloadModelAsync("cancel-test", new[] { SynthOptions.BaseUrl + "large.gguf" }, _tempTestDir, progress, cts.Token);
        });

        // Strict Physics Validation: Ensure the FileStream was disposed properly.
        // If the stream is still locked by a background thread, File.Delete will throw IOException.
        string expectedFile = Path.Combine(_tempTestDir, "large.gguf.tmp");
        var exception = Record.Exception(() => File.Delete(expectedFile));
        Assert.Null(exception); // Should cleanly delete without I/O lock errors
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempTestDir))
        {
            try { Directory.Delete(_tempTestDir, true); } catch { /* Ignore cleanup errors */ }
        }
    }
}
