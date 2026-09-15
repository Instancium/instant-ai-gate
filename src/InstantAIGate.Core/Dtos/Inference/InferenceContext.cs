namespace InstantAIGate.Core.Dtos.Inference;

/// <summary>
/// Aggregates text and vision contexts for inference operations.
/// </summary>
public sealed class InferenceContext : IDisposable
{
    private bool _disposed;

    /// <summary>
    /// Text inference context.
    /// </summary>
    public ModelContext TextContext { get; }

    /// <summary>
    /// Optional vision context for multimodal inference.
    /// </summary>
    public VisionContext? VisionContext { get; }

    /// <summary>
    /// Initializes a new instance of the inference context.
    /// </summary>
    /// <param name="textContext">Text inference context.</param>
    /// <param name="visionContext">Optional vision context.</param>
    public InferenceContext(ModelContext textContext, VisionContext? visionContext)
    {
        TextContext = textContext ?? throw new ArgumentNullException(nameof(textContext));
        VisionContext = visionContext;
    }

    /// <summary>
    /// Disposes the inference context and releases resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;

        TextContext.Dispose();
        VisionContext?.Dispose();
        _disposed = true;
    }
}