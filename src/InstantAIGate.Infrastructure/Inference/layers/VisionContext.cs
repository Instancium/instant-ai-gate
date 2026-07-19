using InstantAIGate.Infrastructure.Inference.Native;

namespace InstantAIGate.Infrastructure.Inference.layers
{
    /// <summary>
    /// Manages the lifecycle of a native MTMD context and ensures proper resource release.
    /// </summary>
    public sealed class VisionContext : IDisposable
    {
        private IntPtr _handle;
        private bool _disposed;
        private readonly object _lock = new();

        /// <summary>
        /// Gets the native handle to the MTMD context.
        /// </summary>
        public IntPtr Handle
        {
            get
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                return _handle;
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="VisionContext"/> class.
        /// </summary>
        /// <param name="handle">The native pointer returned by mtmd_init_from_file.</param>
        public VisionContext(IntPtr handle)
        {
            if (handle == IntPtr.Zero)
            {
                throw new ArgumentException("Native handle cannot be zero.", nameof(handle));
            }
            _handle = handle;
        }

        /// <summary>
        /// Releases the unmanaged resources used by the MTMD context.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Finalizer to ensure native resources are released if Dispose is not called.
        /// </summary>
        ~VisionContext()
        {
            Dispose(false);
        }

        private void Dispose(bool disposing)
        {
            lock (_lock)
            {
                if (_disposed) return;

                if (_handle != IntPtr.Zero)
                {
                    // Direct call to NativeMethods as per isolation requirement
                    NativeMtmdMethods.Free(_handle);
                    _handle = IntPtr.Zero;
                }

                _disposed = true;
            }
        }
    }
}