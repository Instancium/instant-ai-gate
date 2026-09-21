using InstantAIGate.Cli.Commands;
using InstantAIGate.Cli.Logging;
using InstantAIGate.Cli.State;
using InstantAIGate.Core.Interfaces.Inference;
using InstantAIGate.Native.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using System;
using System.Text;
using System.Threading.Tasks;
using InstantAIGate.SSR.DependencyInjection;

namespace InstantAIGate.Cli;

public static class Program
{
    public static async Task Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;
        

        // 1. Capture the original, unfiltered console streams
        var originalOut = Console.Out;
        var originalError = Console.Error;

        // 2. Create the shared debug state
        var debugState = new DebugState();
        //debugState.IsEnabled = false;

        // 3. Apply the filters globally so the Core writes into the Black Hole
        Console.SetOut(new EngineLogFilter(originalOut, debugState));
        Console.SetError(new EngineLogFilter(originalError, debugState));

        // 4. Force Spectre.Console to bypass the filter and draw to the real screen
        AnsiConsole.Console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(originalOut)
        });

        var host = Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((context, config) =>
            {
                config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
            })
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders(); // Remove default console logger
                logging.Services.AddSingleton<ILoggerProvider>(sp =>
                {
                    var debugState = sp.GetRequiredService<DebugState>();
                    return new DebugStateLoggerProvider(debugState);
                });
            })
            .ConfigureServices((context, services) =>
            {
                // Register Core Native Inference (Vulkan backend)
                services.AddInstantAIGateInference();
                services.AddInstantAIGateSSR();
                //services.AddSingleton<IModelPathProvider>(new LocalModelPathProvider("C:\\models\\Qwen_Qwen3-VL-8B-Instruct-GGUF"));

                // Register CLI State & Debug State
                services.AddSingleton<CliSession>();
                services.AddSingleton(debugState); // Make debug state available to commands

                // Register Commands
                services.AddSingleton<CommandDispatcher>();
                services.AddTransient<IConsoleCommand, LoadCommand>();
                services.AddTransient<IConsoleCommand, ImageCommand>();
                services.AddTransient<IConsoleCommand, HelpCommand>();
                services.AddTransient<IConsoleCommand, DebugCommand>(); // Register the toggle

                // Register Main Loop
                services.AddHostedService<CliHostedService>();
            })
            .Build();

        await host.RunAsync();
    }
}