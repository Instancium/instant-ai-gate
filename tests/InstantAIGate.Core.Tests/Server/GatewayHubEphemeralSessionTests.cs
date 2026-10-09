namespace InstantAIGate.Core.Tests.Server;

using InstantAIGate.Core.Interfaces.Inference;
using InstantAIGate.Core.Tests.TestConfiguration;
using InstantAIGate.Server;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

public class GatewayHubEphemeralSessionTests : IClassFixture<GatewayHubEphemeralSessionTests.EphemeralSessionTestFixture>
{
    private readonly EphemeralSessionTestFixture _fixture;

    public GatewayHubEphemeralSessionTests(EphemeralSessionTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task MarkSessionAsEphemeral_OnDisconnected_CallsReleaseSessionWithDestroySlotTrue()
    {
        var mockSessionManager = _fixture.MockSessionInferenceManager;
        var connection = _fixture.CreateGatewayConnection(_fixture.ServerOptions.TenantApiKey);
        await connection.StartAsync();

        const string sessionId = "ephemeral-session-1";
        await connection.InvokeAsync("MarkSessionAsEphemeral", sessionId);

        await connection.StopAsync();

        await Task.Delay(500);

        mockSessionManager.Verify(
            m => m.ReleaseSessionAsync(sessionId, true, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    public class EphemeralSessionTestFixture : WebApplicationFactory<Program>
    {
        public Mock<ISessionInferenceManager> MockSessionInferenceManager { get; } = new();
        public TestServerOptions ServerOptions { get; } = TestConfig.Server;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(ISessionInferenceManager));
                if (descriptor != null)
                {
                    services.Remove(descriptor);
                }
                services.AddSingleton(MockSessionInferenceManager.Object);
            });
        }

        public HubConnection CreateGatewayConnection(string token)
        {
            var gatewayUrl = new Uri(new Uri(ServerOptions.PublicBaseUrl), "/hub/gateway");
            return new HubConnectionBuilder()
                .WithUrl(gatewayUrl, options =>
                {
                    options.HttpMessageHandlerFactory = _ => Server.CreateHandler();
                    options.AccessTokenProvider = () => Task.FromResult(token)!;
                })
                .Build();
        }
    }
}