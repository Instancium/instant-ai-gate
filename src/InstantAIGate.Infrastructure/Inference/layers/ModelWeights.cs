using InstantAIGate.Application.Interfaces.Inference;
using InstantAIGate.Infrastructure.Inference.Native;

namespace InstantAIGate.Infrastructure.Inference.layers
{
    /// <summary>
    /// Managed wrapper around a native llama_model pointer.
    /// Implements the application-level inference model interface.
    /// </summary>
    public sealed class ModelWeights : IDisposable
    {
        public IntPtr Handle { get; private set; }

        private readonly bool _isOwned;
        private readonly Action? _onRelease;
        private readonly NativeLlamaApi _nativeApi;

        public ModelWeights(IntPtr handle, bool isOwned, NativeLlamaApi nativeApi, Action? onRelease = null)
        {
            Handle = handle;
            _isOwned = isOwned;
            _nativeApi = nativeApi;
            _onRelease = onRelease;
        }

        public void Dispose()
        {
            if (Handle != IntPtr.Zero)
            {
                _onRelease?.Invoke();
                if (_isOwned) _nativeApi.FreeModel(Handle);
                Handle = IntPtr.Zero;
            }
        }
    }
}