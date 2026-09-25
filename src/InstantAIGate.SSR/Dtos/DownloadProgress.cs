namespace InstantAIGate.SSR.Dtos
{
    public record DownloadProgress(
        string ModelId,
        long BytesDownloaded,
        long TotalBytes,
        double SpeedBytesPerSecond,
        float Percentage
    );
}
