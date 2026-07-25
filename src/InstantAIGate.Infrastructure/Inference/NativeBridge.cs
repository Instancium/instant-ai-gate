using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using InstantAIGate.Application.Interfaces.Inference;

namespace InstantAIGate.Infrastructure.Inference
{
    internal static class NativeBridge
    {
        [DllImport("InstantAIGateBridge", CallingConvention = CallingConvention.Cdecl)]
        public static extern void c_EnqueueTask([MarshalAs(UnmanagedType.LPStr)] string requestId, int[] tokens, int tokenCount);

        [DllImport("InstantAIGateBridge", CallingConvention = CallingConvention.Cdecl)]
        public static extern int c_AwaitResponses(out IntPtr requestIdPtr, out IntPtr tokenPtr, out int tokenCount);
    }

    public class TrtLlmBackend : IInferenceBackend, IDisposable
    {
        private readonly Task _routerTask;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly ConcurrentDictionary<string, ChannelWriter<int>> _activeRequests;
        private bool _disposed;

        public TrtLlmBackend()
        {
            _activeRequests = new ConcurrentDictionary<string, ChannelWriter<int>>();
            _cancellationTokenSource = new CancellationTokenSource();

            _routerTask = Task.Factory.StartNew(
                () => RouteResponsesAsync(_cancellationTokenSource.Token),
                _cancellationTokenSource.Token,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
        }

        public Task ExecuteInferenceAsync(string requestId, int[] tokens, ChannelWriter<int> responseWriter, CancellationToken cancellationToken)
        {
            _activeRequests.TryAdd(requestId, responseWriter);

            cancellationToken.Register(() =>
            {
                _activeRequests.TryRemove(requestId, out _);
            });

            try
            {
                NativeBridge.c_EnqueueTask(requestId, tokens, tokens.Length);
            }
            catch (Exception ex)
            {
                _activeRequests.TryRemove(requestId, out _);
                responseWriter.TryComplete(ex);
                throw;
            }

            return Task.CompletedTask;
        }

        private async Task RouteResponsesAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    int status = NativeBridge.c_AwaitResponses(out IntPtr requestIdPtr, out IntPtr tokenPtr, out int tokenCount);

                    if (status == 1 && requestIdPtr != IntPtr.Zero && tokenPtr != IntPtr.Zero)
                    {
                        string requestId = Marshal.PtrToStringAnsi(requestIdPtr) ?? string.Empty;

                        if (_activeRequests.TryGetValue(requestId, out ChannelWriter<int> writer))
                        {
                            int[] tokens = new int[tokenCount];
                            Marshal.Copy(tokenPtr, tokens, 0, tokenCount);

                            foreach (int token in tokens)
                            {
                                await writer.WriteAsync(token, cancellationToken);
                            }
                        }
                    }
                    else
                    {
                        await Task.Delay(1, cancellationToken);
                    }
                }
                catch (DllNotFoundException)
                {
                    await Task.Delay(1000, cancellationToken);
                }
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _cancellationTokenSource.Cancel();
            _routerTask.Wait();
            _cancellationTokenSource.Dispose();
            _disposed = true;
        }
    }
}