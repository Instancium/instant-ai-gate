using InstantAIGate.Cli.Commands;
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
using Microsoft.Extensions.Options;
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

        var originalOut = Console.Out;
        var originalError = Console.Error;

        var debugState = new DebugState();

        Console.SetOut(new EngineLogFilter(originalOut, debugState));
        Console.SetError(new EngineLogFilter(originalError, debugState));

        AnsiConsole.Console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(originalOut)
        });


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
                logging.ClearProviders();
                logging.Services.AddSingleton<ILoggerProvider>(sp => new DebugStateLoggerProvider(debugState));
            })
            .ConfigureServices((context, services) =>
            {
                services.Configure<StorageSettings>(context.Configuration.GetSection("InstantAIGate:Storage"));
                services.AddSingleton<CliSession>();
                services.AddSingleton(debugState);

                services.AddSingleton<CommandDispatcher>();
                services.AddTransient<IConsoleCommand, LoadCommand>();
                services.AddTransient<IConsoleCommand, ImageCommand>();
                services.AddTransient<IConsoleCommand, HelpCommand>();
                services.AddTransient<IConsoleCommand, DebugCommand>();
                services.AddTransient<IConsoleCommand, ModelsCommand>();
                services.AddTransient<IConsoleCommand, DownloadCommand>();

      
                services.AddTransient<IConsoleCommand, ConnectCommand>();

                services.AddHttpClient();
                services.AddInstantAIGateInference();
                services.AddInstantAIGateSSR();
                services.AddSingleton<LocalGatewayClient>();

     
                services.AddSingleton<GatewayClientProxy>(sp =>
                {
                    IGatewayClient initial;
                    var httpClientFactory = sp.GetRequiredService<System.Net.Http.IHttpClientFactory>();

                    if (isRemote)
                    {
                        initial = new RemoteGatewayClient(httpClientFactory.CreateClient(), remoteUrl, adminKey);
                    }
                    else
                    {
                        initial = sp.GetRequiredService<LocalGatewayClient>();
                    }

                    return new GatewayClientProxy(sp, httpClientFactory, initial);
                });

    
                services.AddSingleton<IGatewayClient>(sp => sp.GetRequiredService<GatewayClientProxy>());

                services.AddHostedService<CliHostedService>();
            })
            .Build();

        var storageConfig = host.Services.GetRequiredService<IOptions<StorageSettings>>().Value;
        _ = storageConfig.ModelsDirectory;

        await host.RunAsync();
    }
}