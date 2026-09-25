// File: src/InstantAIGate.Core/Dtos/Inference/VisionContext.cs
namespace InstantAIGate.Core.Dtos.Inference
{
    using InstantAIGate.Core.Interfaces.Native;
    using System;

    public sealed class VisionContext : IDisposable
    {
        private bool _disposed;
        private readonly IVisionHandle _handle;
        private readonly Action<IVisionHandle> _onDispose;

        public IVisionHandle Handle => _handle;

        public VisionContext(IVisionHandle handle, Action<IVisionHandle> onDispose)
        {
            _handle = handle ?? throw new ArgumentNullException(nameof(handle));
            _onDispose = onDispose ?? throw new ArgumentNullException(nameof(onDispose));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _onDispose(_handle);
            _disposed = true;
        }
    }
}