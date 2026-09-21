using InstantAIGate.Core.Tests.Infrastructure;
using InstantAIGate.SSR.Downloader;
using InstantAIGate.SSR.Dtos;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;

namespace InstantAIGate.Core.Tests.Downloader
{
    public class ParallelModelDownloaderTests
    {
        [Fact]
        public async Task DownloadModelAsync_MultiThreaded_CalculatesSpeedCorrectly()
        {
            // Arrange
            var syntheticHandler = new SyntheticNetworkHandler(virtualFileSizeBytes: 1024 * 1024 * 100); // 100 MB virtual file
            var httpClient = new HttpClient(syntheticHandler);
            var downloader = new ParallelModelDownloader(httpClient, NullLogger<ParallelModelDownloader>.Instance);

            var progressList = new List<DownloadProgress>();
            var progress = new Progress<DownloadProgress>(p => progressList.Add(p));

            string destDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

            // Act
            await downloader.DownloadModelAsync("test-model", new[] { "http://synthetic-server/model.gguf" }, destDir, progress);

            // Assert
            Assert.NotEmpty(progressList);
            Assert.Equal(100f, progressList.Last().Percentage);
            Assert.True(progressList.Last().SpeedBytesPerSecond > 0);

            // Cleanup
            Directory.Delete(destDir, true);
        }
    }
}
