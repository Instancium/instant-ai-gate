using InstantAIGate.Application.Dtos.Streaming;
using InstantAIGate.Application.Interfaces.Storage;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;

namespace InstantAIGate.Infrastructure.Storage
{
    public class HttpModelStorageService : IModelStorageService
    {
        private readonly IModelPathProvider _pathProvider;
        private readonly ILogger<HttpModelStorageService> _logger;
        private readonly IHttpDownloader _downloader;
        private readonly IFileStorageService _fileStorage;

        public HttpModelStorageService(
            IModelPathProvider pathProvider,
            IHttpDownloader downloader,
            IFileStorageService fileStorage,
            ILogger<HttpModelStorageService> logger)
        {
            _logger = logger;
            _pathProvider = pathProvider;
            _downloader = downloader;
            _fileStorage = fileStorage;
        }

        // Method ensures the storage directory exists and returns its path
        public string GetStoragePath(string repoId)
        {
            var path = _pathProvider.GetModelDirectory(repoId);
            _fileStorage.EnsureDirectoryExists(path);
            return path;
        }

        public async IAsyncEnumerable<DownloadProgress> DownloadModelFileAsync(
            string url,
            string repoId,
            string fileName,
            long expectedSizeBytes,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            var destinationPath = _pathProvider.GetModelFilePath(repoId, fileName);
            var directory = _pathProvider.GetModelDirectory(repoId);

            _fileStorage.EnsureDirectoryExists(directory);

            // If file already exists — report completed
            if (_fileStorage.FileExists(destinationPath))
            {
                var size = _fileStorage.GetFileSize(destinationPath);
                yield return new DownloadProgress(fileName, size, size, 100, true);
                yield break;
            }

            var tempPath = _fileStorage.GetTempPath(destinationPath);
            _fileStorage.DeleteIfExists(tempPath);

            _logger.LogInformation("Starting download {File} from {Url}", fileName, url);

            // Use downloader to get the response, then stream to file
            using var clientResponseStreamProvider = await _downloader.GetResponseStreamAsync(url, ct);

            var response = clientResponseStreamProvider.Response;

            // Validate response and stream to file
            response.EnsureSuccessStatusCode();

            var contentType = response.Content.Headers.ContentType?.MediaType;
            if (contentType == "text/html")
                throw new Exception($"Received HTML instead of binary. URL returned an error page.");

            var totalBytes = response.Content.Headers.ContentLength ?? -1L;

            if (expectedSizeBytes > 0 && totalBytes > 0 && totalBytes < expectedSizeBytes * 0.9)
            {
                throw new Exception($"File too small. Expected ~{expectedSizeBytes} bytes, got {totalBytes}.");
            }

            using var downloadStream = clientResponseStreamProvider.Stream;
            await using var fileStream = new FileStream(
                tempPath, FileMode.Create, FileAccess.Write, FileShare.None,
                bufferSize: 256 * 1024, useAsync: true);

            var buffer = new byte[256 * 1024];
            long totalRead = 0;
            int read;

            while ((read = await downloadStream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, read), ct);
                totalRead += read;

                var percent = totalBytes > 0 ? (double)totalRead / totalBytes * 100.0 : 0.0;
                yield return new DownloadProgress(fileName, totalBytes, totalRead, percent, false);
            }

            fileStream.Close();

            _fileStorage.MoveFileAtomic(tempPath, destinationPath);

            _logger.LogInformation("File {File} successfully downloaded to {Path}", fileName, destinationPath);
            yield return new DownloadProgress(fileName, totalBytes, totalRead, 100, true);
        }
    }
}