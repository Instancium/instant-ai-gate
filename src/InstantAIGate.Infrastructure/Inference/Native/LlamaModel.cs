using InstantAIGate.Application.Interfaces.Inference;

namespace InstantAIGate.Infrastructure.Inference.Native
{
    /// <summary>
    /// Managed wrapper around a native llama_model pointer.
    /// Implements the application-level inference model interface.
    /// </summary>
    public sealed class LlamaModel : IInferenceModel
    {
        public IntPtr Handle { get; private set; }

        private readonly bool _isOwned;
        private readonly Action? _onRelease;
        private readonly INativeLlamaApi _nativeApi;

        public LlamaModel(IntPtr handle, bool isOwned, INativeLlamaApi nativeApi, Action? onRelease = null)
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