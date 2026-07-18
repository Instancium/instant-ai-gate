namespace InstantAIGate.Infrastructure.Inference.Vision
{
    /// <summary>
    /// Represents a decoded image ready for native memory allocation.
    /// </summary>
    public record RgbImageResult(uint Width, uint Height, byte[] RgbData);

    /// <summary>
    /// Resolves image URLs (HTTP/Base64) into raw RGB pixel arrays.
    /// </summary>
    public interface IImageContentResolver
    {
        Task<RgbImageResult> ResolveAsync(string imageUrl, CancellationToken ct = default);
    }
}
