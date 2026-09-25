namespace InstantAIGate.Core.Interfaces.Inference;

public interface IAssetManager
{
    Task<string> GetOrCacheMediaAsync(string uriOrBase64, CancellationToken ct = default);
    void Invalidate(string uriOrBase64);
    void Clear();
}