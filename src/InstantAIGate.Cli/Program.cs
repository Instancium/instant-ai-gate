namespace InstantAIGate.Cli;

using System;
using System.Threading;
using System.Threading.Tasks;
using InstantAIGate.Cli.Services;
using InstantAIGate.Core.Dtos.Config;
using InstantAIGate.Core.Interfaces.Inference;
using InstantAIGate.Native.Bindings;
using InstantAIGate.Native.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

/// <summary>
/// Entry point for the InstantAIGate CLI application.
/// </summary>
public class Program
{
    /// <summary>
    /// Main execution entry point.
    /// </summary>
    public static async Task Main(string[] args)
    {

        try
        {
            Console.WriteLine("Loading native libraries...");
            if (!NativeLibraryLoader.Load())
            {
                Console.Error.WriteLine("Failed to load native libraries!");
                return;
            }
            Console.WriteLine("Native libraries loaded successfully.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Native library loading failed: {ex.Message}");
            return;
        }

        var services = new ServiceCollection();
        ConfigureServices(services);

        using var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
        var manager = serviceProvider.GetRequiredService<IModelManager>();

        logger.LogInformation("InstantAIGate CLI started.");

        try
        {
            var config = new ModelSettings
            {
                RepoId = "qwen2.5-7b-instruct-q4_k_m",
                ContextSize = 2048,
                BatchSize = 512,
                GpuLayerCount = 20,
                MainGPU = 0,
                Threads = Environment.ProcessorCount,
                FlashAttention = true,
                KvCacheQuantization = "F16",
                VisionSupport = false,
                MaxContexts = 2
            };

            logger.LogInformation("Loading model: {RepoId}", config.RepoId);
            await manager.LoadModelAsync(config, CancellationToken.None);

            var metrics = manager.GetMetrics();
            logger.LogInformation(
                "Model loaded. Active leases: {Leases}, Pending: {Pending}",
                metrics.ActiveLeases,
                metrics.PendingRequests);

            logger.LogInformation("Acquiring context for inference...");
            using var context = await manager.AcquireContextAsync(config.RepoId, CancellationToken.None);

            logger.LogInformation("Context acquired successfully. Text Handle: {Handle}", context.TextContext.Handle);
            logger.LogInformation("Press any key to exit and trigger graceful shutdown...");
            Console.ReadKey();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Fatal error during CLI execution.");
        }
        finally
        {
        
            NativeLibraryLoader.Unload();
        }
    }

    /// <summary>
    /// Configures dependency injection container.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        // Register all core and native inference services
        services.AddInstantAIGateInference();

        // Register environment-specific path provider
        services.AddSingleton<IModelPathProvider>(new LocalModelPathProvider("./models"));
    }
}