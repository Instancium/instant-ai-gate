using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Xunit;
using InstantAIGate.Infrastructure.Inference;

namespace InstantAIGate.Infrastructure.Tests.Inference
{
    public class TrtLlmBackendTests : IDisposable
    {
        private readonly TrtLlmBackend _backend;

        public TrtLlmBackendTests()
        {
            _backend = new TrtLlmBackend();
        }

        [Fact]
        public async Task ExecuteInferenceAsync_RegistersRequest()
        {
            string requestId = Guid.NewGuid().ToString();
            Channel<int> channel = Channel.CreateUnbounded<int>();
            using CancellationTokenSource cts = new CancellationTokenSource();

            await _backend.ExecuteInferenceAsync(requestId, Array.Empty<int>(), channel.Writer, cts.Token);

            ConcurrentDictionary<string, ChannelWriter<int>> dictionary = GetActiveRequests(_backend);
            Assert.True(dictionary.ContainsKey(requestId));
        }

        [Fact]
        public async Task ExecuteInferenceAsync_CancellationRemovesRequest()
        {
            string requestId = Guid.NewGuid().ToString();
            Channel<int> channel = Channel.CreateUnbounded<int>();
            using CancellationTokenSource cts = new CancellationTokenSource();

            await _backend.ExecuteInferenceAsync(requestId, Array.Empty<int>(), channel.Writer, cts.Token);

            cts.Cancel();

            ConcurrentDictionary<string, ChannelWriter<int>> dictionary = GetActiveRequests(_backend);
            Assert.False(dictionary.ContainsKey(requestId));
        }

        [Fact]
        public void Dispose_CancelsRouterTaskAndCleansUp()
        {
            _backend.Dispose();

            FieldInfo ctsField = typeof(TrtLlmBackend).GetField("_cancellationTokenSource", BindingFlags.NonPublic | BindingFlags.Instance);
            CancellationTokenSource cts = (CancellationTokenSource)ctsField.GetValue(_backend);

            Assert.True(cts.IsCancellationRequested);
        }

        private ConcurrentDictionary<string, ChannelWriter<int>> GetActiveRequests(TrtLlmBackend backend)
        {
            FieldInfo field = typeof(TrtLlmBackend).GetField("_activeRequests", BindingFlags.NonPublic | BindingFlags.Instance);
            return (ConcurrentDictionary<string, ChannelWriter<int>>)field.GetValue(backend);
        }

        public void Dispose()
        {
            _backend.Dispose();
        }
    }
}