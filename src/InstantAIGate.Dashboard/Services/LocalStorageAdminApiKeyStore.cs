namespace InstantAIGate.Dashboard.Services;

using Microsoft.JSInterop;

public sealed class LocalStorageAdminApiKeyStore : IAdminApiKeyStore
{
    private const string StorageKey = "instantaigate.adminApiKey";
    private readonly IJSRuntime _jsRuntime;

    public LocalStorageAdminApiKeyStore(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime ?? throw new ArgumentNullException(nameof(jsRuntime));
    }

    public async ValueTask<string?> GetApiKeyAsync(CancellationToken ct = default)
    {
        try
        {
            return await _jsRuntime.InvokeAsync<string?>("localStorage.getItem", ct, StorageKey);
        }
        catch (JSException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public async ValueTask SetApiKeyAsync(string apiKey, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(apiKey);
        await _jsRuntime.InvokeVoidAsync("localStorage.setItem", ct, StorageKey, apiKey);
    }

    public async ValueTask ClearAsync(CancellationToken ct = default)
    {
        await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", ct, StorageKey);
    }
}
