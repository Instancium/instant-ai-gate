namespace InstantAIGate.Infrastructure.Inference.Vision
{
    /// <summary>
    /// Disposable wrapper for the native MTMD context.
    /// </summary>
    public sealed class MtmdClipContext : IDisposable
    {
        public IntPtr Handle { get; }
        private readonly Action _onRelease;

        public MtmdClipContext(IntPtr handle, Action onRelease)
        {
            Handle = handle;
            _onRelease = onRelease;
        }

        public void Dispose() => _onRelease?.Invoke();
    }
}
