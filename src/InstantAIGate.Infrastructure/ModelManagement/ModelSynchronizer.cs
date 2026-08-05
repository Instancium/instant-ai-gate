using System.Runtime.CompilerServices;
using InstantAIGate.Application.ModelManagement;
using InstantAIGate.Application.ModelManagement.Conteracts;
using InstantAIGate.Domain.Entities;

namespace InstantAIGate.Infrastructure.ModelManagement
{
    public class ModelSynchronizer : IModelSynchronizer
    {
        private readonly IModelManifestResolver _resolver;
        private readonly IFileStorageService _storageService;
        private readonly HttpClient _httpClient;
        private readonly string _baseModelsDirectory = "models_cache";

        public ModelSynchronizer(IModelManifestResolver resolver, IFileStorageService storageService, HttpClient httpClient)
        {
            _resolver = resolver;
            _storageService = storageService;
            _httpClient = httpClient;
        }

        public async IAsyncEnumerable<AggregateDownloadProgress> SynchronizeModelAsync(
            SupportedModelDefinition definition,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var filesEnumerable = await _resolver.ResolveManifestAsync(definition, cancellationToken);
            var modelFiles = filesEnumerable.ToList();

            long totalModelSizeBytes = modelFiles.Sum(f => f.SizeBytes);
            long totalDownloadedBytes = 0;

            string targetDirectory = Path.Combine(_baseModelsDirectory, definition.RepoId.Replace("/", "_"));
            string tempDirectory = _storageService.GetTempPath(targetDirectory);

            _storageService.EnsureDirectoryExists(tempDirectory);

            foreach (var file in modelFiles)
            {
                string fileTempPath = Path.Combine(tempDirectory, file.RelativePath);
                string fileTempDir = Path.GetDirectoryName(fileTempPath) ?? string.Empty;

                _storageService.EnsureDirectoryExists(fileTempDir);

                if (_storageService.FileExists(fileTempPath))
                {
                    long localSize = _storageService.GetFileSize(fileTempPath);
                    if (localSize == file.SizeBytes)
                    {
                        totalDownloadedBytes += localSize;
                        yield return new AggregateDownloadProgress
                        {
                            TotalDownloadedBytes = totalDownloadedBytes,
                            TotalModelSizeBytes = totalModelSizeBytes
                        };
                        continue;
                    }
                    else
                    {
                        _storageService.DeleteIfExists(fileTempPath);
                    }
                }

                using var request = new HttpRequestMessage(HttpMethod.Get, file.Url);
                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();

                using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var fileStream = new FileStream(fileTempPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                byte[] buffer = new byte[8192];
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                    totalDownloadedBytes += bytesRead;

                    yield return new AggregateDownloadProgress
                    {
                        TotalDownloadedBytes = totalDownloadedBytes,
                        TotalModelSizeBytes = totalModelSizeBytes
                    };
                }
            }

            _storageService.MoveDirectoryAtomic(tempDirectory, targetDirectory);
        }
    }
}