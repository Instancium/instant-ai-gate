namespace InstantAIGate.Core.Dtos.Inference;

using InstantAIGate.Core.Interfaces.Inference;

/// <summary>
/// Provides access to model weights for direct operations.
/// </summary>
public sealed class ModelWeights : IDisposable
{
    private bool _disposed;
    private readonly IntPtr _handle;
    private readonly bool _isOwned;
    private readonly IBackendFacade? _backendFacade;

    /// <summary>
    /// Native model handle.
    /// </summary>
    public IntPtr Handle => _handle;

    /// <summary>
    /// Indicates whether this instance owns the native resource.
    /// </summary>
    public bool IsOwned => _isOwned;

    /// <summary>
    /// Initializes a new instance of the model weights accessor.
    /// </summary>
    /// <param name="handle">Native model handle.</param>
    /// <param name="isOwned">Whether this instance owns the resource.</param>
    /// <param name="backendFacade">Backend facade for resource cleanup.</param>
    public ModelWeights(IntPtr handle, bool isOwned, IBackendFacade? backendFacade = null)
    {
        _handle = handle;
        _isOwned = isOwned;
        _backendFacade = backendFacade;
    }

    /// <summary>
    /// Disposes the model weights and releases resources if owned.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;

        if (_isOwned && _backendFacade != null)
        {
            _backendFacade.FreeModel(_handle);
        }
        _disposed = true;
    }
}