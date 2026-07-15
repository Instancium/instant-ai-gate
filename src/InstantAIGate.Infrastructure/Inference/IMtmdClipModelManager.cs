using InstantAIGate.Infrastructure.Inference.Native;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace InstantAIGate.Infrastructure.Inference
{
    public interface IMtmdClipModelManager
    {
        Task<MtmdClipContext> AcquireContextAsync(string projectorPath, bool useGpu = true, CancellationToken ct = default);
        Task UnloadModelAsync(string projectorPath, CancellationToken ct = default);
    }

    /// <summary>
    /// Disposable wrapper for the native CLIP context.
    /// Ensures the access semaphore is released when the context is no longer in use.
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

        public void Dispose()
        {
            _onRelease?.Invoke();
        }
    }
}