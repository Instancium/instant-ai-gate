namespace InstantAIGate.Core.Dtos.Inference;

/// <summary>
/// Wrapper for multimodal vision context.
/// </summary>
public sealed class VisionContext : IDisposable
{
    private bool _disposed;
    private readonly IntPtr _handle;
    private readonly Action<IntPtr> _onDispose;

    /// <summary>
    /// Native vision context handle.
    /// </summary>
    public IntPtr Handle => _handle;

    /// <summary>
    /// Initializes a new instance of the vision context.
    /// </summary>
    /// <param name="handle">Native vision context handle.</param>
    /// <param name="onDispose">Callback to execute on disposal.</param>
    public VisionContext(IntPtr handle, Action<IntPtr> onDispose)
    {
        _handle = handle;
        _onDispose = onDispose ?? throw new ArgumentNullException(nameof(onDispose));
    }

    /// <summary>
    /// Disposes the vision context and releases resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;

        _onDispose(_handle);
        _disposed = true;
    }
}