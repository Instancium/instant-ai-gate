namespace InstantAIGate.Core.Tests.Downloader;

using InstantAIGate.Core.Tests.Inference.Stubs;
using InstantAIGate.Core.Tests.Infrastructure;
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
    private readonly string _tempTestDir;

    public ParallelModelDownloaderTests()
    {
        // Ensure clean I/O environment for each test
        _tempTestDir = Path.Combine(Path.GetTempPath(), "InstantAIGate_Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempTestDir);
    }

    [Fact]
    public async Task DownloadModelAsync_MultiThreaded_CalculatesSpeedAndCompletes()
    {
        // Arrange
        var stubValidator = new StubModelValidator { ShouldPass = true };
        var handler = new SyntheticNetworkHandler(virtualFileSizeBytes: 1024 * 1024 * 50, supportRanges: true);
        var downloader = new ParallelModelDownloader(
            new HttpClient(handler),
            NullLogger<ParallelModelDownloader>.Instance,
            stubValidator);
        var progressList = new List<DownloadProgress>();
        var progress = new Progress<DownloadProgress>(progressList.Add);

        // Act
        await downloader.DownloadModelAsync("test-model", new[] { "http://synth/model.gguf" }, _tempTestDir, progress);

        // Assert
        Assert.NotEmpty(progressList);
        var finalProgress = progressList.Last();
        Assert.Equal(100f, finalProgress.Percentage);
        Assert.Equal(finalProgress.TotalBytes, finalProgress.BytesDownloaded);
        Assert.True(finalProgress.SpeedBytesPerSecond > 0);

        string expectedFile = Path.Combine(_tempTestDir, "model.gguf");
        Assert.True(File.Exists(expectedFile));
        Assert.Equal(1024 * 1024 * 50, new FileInfo(expectedFile).Length);
    }

    [Fact]
    public async Task DownloadModelAsync_WithoutRangeSupport_FallsBackToSequential()
    {
        // Arrange: server explicitly rejects Range requests
        var stubValidator = new StubModelValidator { ShouldPass = true };
        var handler = new SyntheticNetworkHandler(virtualFileSizeBytes: 1024 * 1024 * 20, supportRanges: false);
        var downloader = new ParallelModelDownloader(new HttpClient(handler),
            NullLogger<ParallelModelDownloader>.Instance, stubValidator);
        var progressList = new List<DownloadProgress>();
        var progress = new Progress<DownloadProgress>(progressList.Add);

        // Act
        await downloader.DownloadModelAsync("fallback-model", new[] { "http://synth/sequential.gguf" }, _tempTestDir, progress);

        // Assert
        Assert.NotEmpty(progressList);
        Assert.Equal(100f, progressList.Last().Percentage);

        string expectedFile = Path.Combine(_tempTestDir, "sequential.gguf");
        Assert.True(File.Exists(expectedFile));
        Assert.Equal(1024 * 1024 * 20, new FileInfo(expectedFile).Length);
    }

    [Fact]
    public async Task DownloadModelAsync_WhenCancelled_StopsAndReleasesFileLocks()
    {
        // Arrange: massive virtual file to ensure it doesn't finish before cancellation
        var stubValidator = new StubModelValidator { ShouldPass = true };
        var handler = new SyntheticNetworkHandler(virtualFileSizeBytes: 1024L * 1024 * 1024 * 5); // 5 GB
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
            await downloader.DownloadModelAsync("cancel-test", new[] { "http://synth/large.gguf" }, _tempTestDir, progress, cts.Token);
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