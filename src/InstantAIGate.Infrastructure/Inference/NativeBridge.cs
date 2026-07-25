using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using InstantAIGate.Application.Interfaces.Inference;

namespace InstantAIGate.Infrastructure.Inference
{
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct InferenceRequest
    {
        public IntPtr RequestId;
        public int* Tokens;
        public int TokenCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct InferenceResponse
    {
        public IntPtr RequestId;
        public int* GeneratedTokens;
        public int GeneratedTokenCount;
        public byte IsCompleted;
    }

    internal static unsafe class NativeBridge
    {
        [DllImport("InstantAIGate.Bridge", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr Bridge_InitializeExecutor();

        [DllImport("InstantAIGate.Bridge", CallingConvention = CallingConvention.Cdecl)]
        public static extern byte Bridge_EnqueueTask(IntPtr handle, InferenceRequest* request);

        [DllImport("InstantAIGate.Bridge", CallingConvention = CallingConvention.Cdecl)]
        public static extern InferenceResponse* Bridge_AwaitResponses(IntPtr handle);

        [DllImport("InstantAIGate.Bridge", CallingConvention = CallingConvention.Cdecl)]
        public static extern void Bridge_FreeMemory(IntPtr handle);

        [DllImport("InstantAIGate.Bridge", CallingConvention = CallingConvention.Cdecl)]
        public static extern void Bridge_FreeResponse(InferenceResponse* response);
    }

    public class TrtLlmBackend : IInferenceBackend, IDisposable
    {
        private readonly Task _routerTask;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly ConcurrentDictionary<string, ChannelWriter<int>> _activeRequests;
        private readonly object _initLock = new object();
        private IntPtr _executorHandle;
        private bool _disposed;

        public TrtLlmBackend()
        {
            _activeRequests = new ConcurrentDictionary<string, ChannelWriter<int>>();
            _cancellationTokenSource = new CancellationTokenSource();
            _executorHandle = IntPtr.Zero;

            _routerTask = Task.Factory.StartNew(
                () => RouteResponsesAsync(_cancellationTokenSource.Token),
                _cancellationTokenSource.Token,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
        }

        private void EnsureInitialized()
        {
            if (_executorHandle == IntPtr.Zero)
            {
                lock (_initLock)
                {
                    if (_executorHandle == IntPtr.Zero)
                    {
                        _executorHandle = NativeBridge.Bridge_InitializeExecutor();
                        if (_executorHandle == IntPtr.Zero)
                        {
                            throw new InvalidOperationException("Native bridge returned a null executor handle.");
                        }
                    }
                }
            }
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
                EnsureInitialized();

                unsafe
                {
                    fixed (int* pTokens = tokens)
                    {
                        IntPtr pRequestId = Marshal.StringToHGlobalAnsi(requestId);
                        try
                        {
                            InferenceRequest req = new InferenceRequest
                            {
                                RequestId = pRequestId,
                                Tokens = pTokens,
                                TokenCount = tokens.Length
                            };

                            byte success = NativeBridge.Bridge_EnqueueTask(_executorHandle, &req);
                            if (success == 0)
                            {
                                throw new InvalidOperationException("Failed to enqueue task to native bridge.");
                            }
                        }
                        finally
                        {
                            Marshal.FreeHGlobal(pRequestId);
                        }
                    }
                }
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
                if (_executorHandle == IntPtr.Zero)
                {
                    await Task.Delay(100, cancellationToken);
                    continue;
                }

                try
                {
                    string requestId = string.Empty;
                    int[] managedTokens = Array.Empty<int>();
                    bool isCompleted = false;
                    bool hasData = false;

                    unsafe
                    {
                        InferenceResponse* responsePtr = NativeBridge.Bridge_AwaitResponses(_executorHandle);

                        if (responsePtr != null)
                        {
                            hasData = true;
                            requestId = Marshal.PtrToStringAnsi(responsePtr->RequestId) ?? string.Empty;

                            int count = responsePtr->GeneratedTokenCount;
                            if (count > 0)
                            {
                                managedTokens = new int[count];
                                Marshal.Copy((IntPtr)responsePtr->GeneratedTokens, managedTokens, 0, count);
                            }

                            isCompleted = responsePtr->IsCompleted != 0;
                            NativeBridge.Bridge_FreeResponse(responsePtr);
                        }
                    }

                    if (hasData)
                    {
                        if (_activeRequests.TryGetValue(requestId, out ChannelWriter<int> writer))
                        {
                            foreach (int token in managedTokens)
                            {
                                await writer.WriteAsync(token, cancellationToken);
                            }

                            if (isCompleted)
                            {
                                writer.TryComplete();
                                _activeRequests.TryRemove(requestId, out _);
                            }
                        }
                    }
                    else
                    {
                        await Task.Delay(10, cancellationToken);
                    }
                }
                catch (DllNotFoundException)
                {
                    await Task.Delay(1000, cancellationToken);
                }
                catch (Exception)
                {
                    await Task.Delay(100, cancellationToken);
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

            if (_executorHandle != IntPtr.Zero)
            {
                NativeBridge.Bridge_FreeMemory(_executorHandle);
            }

            _disposed = true;
        }
    }
}