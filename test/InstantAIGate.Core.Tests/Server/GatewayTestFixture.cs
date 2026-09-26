using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace InstantAIGate.Core.Tests.Server;

public class GatewayTestFixture : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((context, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "InstantAIGate:AdminApiKey", "test-admin-secret" },
                { "Kestrel:Endpoints:PublicEndpoint:Url", "http://0.0.0.0:5000" },
                { "Kestrel:Endpoints:AdminEndpoint:Url", "http://0.0.0.0:5001" }
            });
        });

        builder.ConfigureServices(services =>
        {
            services.AddSingleton<IStartupFilter, TestServerPortEmulatorFilter>();
        });
    }
}

public class TestServerPortEmulatorFilter : IStartupFilter
{
    public System.Action<IApplicationBuilder> Configure(System.Action<IApplicationBuilder> next)
    {
        return app =>
        {
            app.Use(async (context, nextDelegate) =>
            {
                // Emulate LocalPort based on the request host for TestServer
                if (context.Request.Host.Port.HasValue)
                {
                    context.Connection.LocalPort = context.Request.Host.Port.Value;
                }
                await nextDelegate();
            });
            next(app);
        };
    }
}