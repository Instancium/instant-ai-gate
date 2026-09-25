namespace InstantAIGate.SSR.Dtos
{
    public record CatalogModelEntry(
        string Id,
        string Name,
        string Architecture,
        ulong ParameterCount,
        string Quantization,
        ulong TotalFileSizeBytes,
        IReadOnlyList<string> DownloadUrls,
        bool RequiresVisionProjector,
        IReadOnlyList<string>? VisionProjectorUrls
    );
}
