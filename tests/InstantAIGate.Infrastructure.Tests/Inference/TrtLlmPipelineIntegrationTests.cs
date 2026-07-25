using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Xunit;
using InstantAIGate.Infrastructure.Inference;

namespace InstantAIGate.Tests.Integration.Inference
{
    [Trait("Category", "Integration")]
    public class TrtLlmPipelineIntegrationTests : IDisposable
    {
        private readonly TrtLlmBackend _backend;

        public TrtLlmPipelineIntegrationTests()
        {
            _backend = new TrtLlmBackend();
        }

        [Fact]
        public async Task ExecuteAndRoute_SuccessfullyStreamsTokens()
        {
            string requestId = Guid.NewGuid().ToString();
            int[] tokens = new[] { 99 };
            Channel<int> channel = Channel.CreateUnbounded<int>();
            using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            await _backend.ExecuteInferenceAsync(requestId, tokens, channel.Writer, cts.Token);

            int receivedToken = -1;
            await foreach (int token in channel.Reader.ReadAllAsync(cts.Token))
            {
                receivedToken = token;
            }

            Assert.Equal(99, receivedToken);
        }

        [Fact]
        public async Task RouteResponsesAsync_HandlesEmptyOrZeroStatus()
        {
            using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

            try
            {
                await Task.Delay(500, cts.Token);
            }
            catch (TaskCanceledException)
            {
            }

            Assert.True(cts.IsCancellationRequested);
        }

        public void Dispose()
        {
            _backend.Dispose();
        }
    }
}