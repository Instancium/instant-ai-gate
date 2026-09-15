namespace InstantAIGate.Core.Dtos.Inference;

/// <summary>
/// Wrapper for text inference context with automatic pooling support.
/// </summary>
public sealed class ModelContext : IDisposable
{
    private bool _disposed;
    private Action<IntPtr>? _onDispose;
    private readonly IntPtr _handle;

    /// <summary>
    /// Native context handle.
    /// </summary>
    public IntPtr Handle => _handle;

    /// <summary>
    /// Initializes a new instance of the model context.
    /// </summary>
    /// <param name="handle">Native context handle.</param>
    /// <param name="onDispose">Callback to execute on disposal.</param>
    public ModelContext(IntPtr handle, Action<IntPtr> onDispose)
    {
        _handle = handle;
        _onDispose = onDispose;
    }

    /// <summary>
    /// Attaches an additional disposal callback.
    /// </summary>
    /// <param name="callback">Callback to attach.</param>
    public void AttachOnDispose(Action callback)
    {
        var originalOnDispose = _onDispose;
        _onDispose = ptr =>
        {
            originalOnDispose?.Invoke(ptr);
            callback?.Invoke();
        };
    }

    /// <summary>
    /// Disposes the context and returns it to the pool.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;

        _onDispose?.Invoke(_handle);
        _disposed = true;
    }
}