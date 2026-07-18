using Microsoft.Extensions.Logging;
using StbImageSharp;

namespace InstantAIGate.Infrastructure.Inference.Services
{
    public class ImageContentResolver : IImageContentResolver
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<ImageContentResolver> _logger;

        private const int MaxImageSizeBytes = 10 * 1024 * 1024;

        public ImageContentResolver(HttpClient httpClient, ILogger<ImageContentResolver> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task<RgbImageResult> ResolveAsync(string imageUrl, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(imageUrl))
                throw new ArgumentException("Image URL cannot be null or empty.");

            byte[] imageBytes = await FetchImageBytesAsync(imageUrl, ct);
            return DecodeToRgb(imageBytes);
        }

        private async Task<byte[]> FetchImageBytesAsync(string imageUrl, CancellationToken ct)
        {

            if (imageUrl.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
            {
                var commaIndex = imageUrl.IndexOf(',');
                if (commaIndex < 0)
                    throw new FormatException("Invalid Base64 image format.");

                string base64Data = imageUrl.Substring(commaIndex + 1);
                return Convert.FromBase64String(base64Data);
            }


            if (imageUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                imageUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("Downloading image from external URL: {Url}", imageUrl);

                using var response = await _httpClient.GetAsync(imageUrl, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();

                var contentLength = response.Content.Headers.ContentLength;
                if (contentLength > MaxImageSizeBytes)
                    throw new InvalidOperationException($"Image exceeds maximum allowed size of {MaxImageSizeBytes / (1024 * 1024)}MB.");

                return await response.Content.ReadAsByteArrayAsync(ct);
            }

            throw new NotSupportedException("Unsupported image URL format. Must be HTTP/HTTPS or Base64 data URI.");
        }

        private RgbImageResult DecodeToRgb(byte[] imageBytes)
        {
            ImageResult image = ImageResult.FromMemory(imageBytes, ColorComponents.RedGreenBlue);

            int width = image.Width;
            int height = image.Height;
            int totalBytes = image.Data.Length;

            _logger.LogDebug("Successfully decoded image: {Width}x{Height} ({TotalBytes} bytes)", width, height, totalBytes);

            // image.Data уже представляет собой готовый плоский массив byte[] в формате RGB!
            return new RgbImageResult((uint)width, (uint)height, image.Data);
        }
    }
}