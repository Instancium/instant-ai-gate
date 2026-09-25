using InstantAIGate.SSR.Dtos;

namespace InstantAIGate.SSR.Contracts
{
    public interface IModelDownloader
    {
        Task DownloadModelAsync(
            string modelId,
            IReadOnlyList<string> downloadUrls,
            string destinationDirectory,
            IProgress<DownloadProgress> progress,
            CancellationToken ct = default);

        Task CancelDownloadAsync(string modelId);
    }
}
