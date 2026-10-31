namespace InstantAIGate.Dashboard.Services;

public interface IAdminApiKeyStore
{
    ValueTask<string?> GetApiKeyAsync(CancellationToken ct = default);
    ValueTask SetApiKeyAsync(string apiKey, CancellationToken ct = default);
    ValueTask ClearAsync(CancellationToken ct = default);
}
