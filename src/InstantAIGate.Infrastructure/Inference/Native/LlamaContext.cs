using InstantAIGate.Application.Interfaces.Inference;

namespace InstantAIGate.Infrastructure.Inference.Native
{
    public sealed class LlamaContext : IInferenceContext
    {
        public IntPtr Handle { get; private set; }
        private readonly Action<LlamaContext>? _returnToPool;

        private readonly List<Action> _onDisposeActions = new();

        public LlamaContext(IntPtr handle, Action<LlamaContext>? returnToPool = null)
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