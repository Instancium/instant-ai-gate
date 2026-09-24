using InstantAIGate.Cli.Core;
using InstantAIGate.Cli.Logging;
using InstantAIGate.Cli.State;
using InstantAIGate.Core.Dtos.Config;
using InstantAIGate.Native.DependencyInjection;
using InstantAIGate.SSR.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace InstantAIGate.Cli;

public static class Program
{
    public static async Task Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;

        var isRemote = args.Contains("--remote");
        var remoteUrl = isRemote ? args[Array.IndexOf(args, "--remote") + 1] : string.Empty;
        var adminKey = isRemote && args.Contains("--key") ? args[Array.IndexOf(args, "--key") + 1] : "test-admin-secret";

        var host = Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((context, config) =>
            {
                config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
            })
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders(); // We manage our own TUI rendering, no standard console logs
            })
            .ConfigureServices((context, services) =>
            {
                var state = new TuiDashboardState
                {
                    ConnectionMode = isRemote ? $"Remote ({remoteUrl})" : "Local Standalone"
                };
                services.AddSingleton(state);

                if (isRemote)
                {
                    services.AddHttpClient();
                    services.AddSingleton<IGatewayClient>(sp =>
                    {
                        var httpClient = sp.GetRequiredService<System.Net.Http.HttpClient>();
                        return new RemoteGatewayClient(httpClient, remoteUrl, adminKey);
                    });
                }
                else
                {
                    // Boot Local Inference Engine
                    services.Configure<StorageSettings>(context.Configuration.GetSection("InstantAIGate:Storage"));
                    services.AddInstantAIGateInference();
                    services.AddInstantAIGateSSR();
                    services.AddSingleton<IGatewayClient, LocalGatewayClient>();
                }

                services.AddHostedService<TuiHostedService>();
            })
            .Build();

        await host.RunAsync();
    }
}