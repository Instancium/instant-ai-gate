using InstantAIGate.Core.Dtos.Config;
using InstantAIGate.Core.Interfaces.Inference;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;


namespace InstantAIGate.Core.Services.Infrastructure;

public sealed class MemoryAssetManager : IAssetManager, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _cacheDirectory;
    private readonly ILogger<MemoryAssetManager> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public MemoryAssetManager(HttpClient httpClient, IOptions<StorageSettings> storageSettings, ILogger<MemoryAssetManager> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _cacheDirectory = Path.Combine(storageSettings.Value.ModelsDirectory, "_assets");

        if (!Directory.Exists(_cacheDirectory))
        {
            Directory.CreateDirectory(_cacheDirectory);
        }
    }

    public async Task<string> GetOrCacheMediaAsync(string uriOrBase64, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(uriOrBase64))
            throw new ArgumentException("Media URI or Base64 string cannot be empty.", nameof(uriOrBase64));

        if (File.Exists(uriOrBase64))
            return uriOrBase64;

        string hash = ComputeHash(uriOrBase64);
        string filePath = Path.Combine(_cacheDirectory, $"{hash}.bin");

        if (File.Exists(filePath))
        {
            _logger.LogDebug("Asset cache hit for hash: {Hash}", hash);
            return filePath;
        }

        var asyncLock = _locks.GetOrAdd(hash, _ => new SemaphoreSlim(1, 1));
        await asyncLock.WaitAsync(ct);
        try
        {
            if (File.Exists(filePath))
                return filePath;

            _logger.LogInformation("Asset cache miss. Fetching/Decoding asset to {FilePath}", filePath);

            if (uriOrBase64.StartsWith("data:image", StringComparison.OrdinalIgnoreCase))
            {
                var commaIndex = uriOrBase64.IndexOf(',');
                if (commaIndex < 0) throw new FormatException("Invalid base64 data URI format.");
                var base64Data = uriOrBase64.Substring(commaIndex + 1);
                byte[] bytes = Convert.FromBase64String(base64Data);
                await File.WriteAllBytesAsync(filePath, bytes, ct);
            }
            else if (uriOrBase64.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                using var response = await _httpClient.GetAsync(uriOrBase64, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();
                using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
                await response.Content.CopyToAsync(fs, ct);
            }
            else
            {
                try
                {
                    byte[] bytes = Convert.FromBase64String(uriOrBase64);
                    await File.WriteAllBytesAsync(filePath, bytes, ct);
                }
                catch (FormatException)
                {
                    throw new NotSupportedException("Unrecognized media format. Must be URL, Data URI, or raw Base64.");
                }
            }

            return filePath;
        }
        finally
        {
            asyncLock.Release();
        }
    }

    public void Invalidate(string uriOrBase64)
    {
        string hash = ComputeHash(uriOrBase64);
        string filePath = Path.Combine(_cacheDirectory, $"{hash}.bin");

        if (File.Exists(filePath))
        {
            try
            {
                File.Delete(filePath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete cached asset: {FilePath}", filePath);
            }
        }
    }

    public void Clear()
    {
        try
        {
            if (Directory.Exists(_cacheDirectory))
            {
                var files = Directory.GetFiles(_cacheDirectory, "*.bin");
                foreach (var file in files)
                {
                    File.Delete(file);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear asset cache.");
        }
    }

    private static string ComputeHash(string input)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public void Dispose()
    {
        foreach (var lck in _locks.Values)
        {
            lck.Dispose();
        }
        _locks.Clear();
    }
}