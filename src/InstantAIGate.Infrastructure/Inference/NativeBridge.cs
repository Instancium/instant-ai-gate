using InstantAIGate.Application.Interfaces.Inference;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Channels;

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

        public TrtLlmBackend()
        {
            _activeRequests = new ConcurrentDictionary<string, ChannelWriter<int>>();
            _cancellationTokenSource = new CancellationTokenSource();

            // Technical documentation: Isolate the blocking C-API calls in a dedicated long-running background thread 
            // to prevent thread-pool starvation and ensure high throughput for token gathering.
            _routerTask = Task.Factory.StartNew(
                () => RouteResponsesAsync(_cancellationTokenSource.Token),
                _cancellationTokenSource.Token,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
        }

        public Task ExecuteInferenceAsync(string requestId, int[] tokens, ChannelWriter<int> responseWriter, CancellationToken cancellationToken)
        {
            _activeRequests.TryAdd(requestId, responseWriter);

            NativeBridge.c_EnqueueTask(requestId, tokens, tokens.Length);

            cancellationToken.Register(() =>
            {
                _activeRequests.TryRemove(requestId, out _);
            });

            return Task.CompletedTask;
        }

        private async Task RouteResponsesAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
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
        }

        public void Dispose()
        {
            _cancellationTokenSource.Cancel();
            _routerTask.Wait();
            _cancellationTokenSource.Dispose();
        }
    }

    public class SimpleBpeTokenizer
    {
        private readonly Dictionary<string, int> _vocab;

        public SimpleBpeTokenizer(Dictionary<string, int> vocab)
        {
            _vocab = vocab;
        }

        public int[] Encode(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return Array.Empty<int>();
            }

            List<int> tokens = new List<int>();
            string[] words = text.Split(' ');

            // Technical documentation: Greedy word-level fallback matching.
            // Replaces standard BPE merge rules for immediate functional integration.
            foreach (string word in words)
            {
                if (_vocab.TryGetValue(word, out int tokenId))
                {
                    tokens.Add(tokenId);
                }
                else
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(word);
                    foreach (byte b in bytes)
                    {
                        if (_vocab.TryGetValue(((char)b).ToString(), out int byteTokenId))
                        {
                            tokens.Add(byteTokenId);
                        }
                    }
                }
            }

            return tokens.ToArray();
        }
    }
}
