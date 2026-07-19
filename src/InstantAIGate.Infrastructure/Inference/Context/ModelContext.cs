

namespace InstantAIGate.Infrastructure.Inference.Context
{
    public sealed class ModelContext : IDisposable
    {
        public IntPtr Handle { get; private set; }
        private readonly Action<ModelContext>? _returnToPool;

        private readonly List<Action> _onDisposeActions = new();

        public ModelContext(IntPtr handle, Action<ModelContext>? returnToPool = null)
        {
            Handle = handle;
            _returnToPool = returnToPool;
        }

        /// <summary>
        /// Allows the manager to attach an action (e.g., releasing a semaphore) 
        /// to be executed when the context is disposed.
        /// </summary>
        public void AttachOnDispose(Action action)
        {
            _onDisposeActions.Add(action);
        }

        public void Dispose()
        {
            if (Handle != IntPtr.Zero)
            {
                _returnToPool?.Invoke(this);

                foreach (var action in _onDisposeActions)
                {
                    try { action.Invoke(); } catch { }
                }

                Handle = IntPtr.Zero;
            }
        }
    }
}