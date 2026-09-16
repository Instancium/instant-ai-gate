namespace InstantAIGate.Cli;

using InstantAIGate.Cli.Services;
using InstantAIGate.Core.Dtos.Config;
using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Interfaces.Inference;
using InstantAIGate.Native.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Entry point for the InstantAIGate CLI application.
/// </summary>
public class Program
{
    /// <summary>
    /// Main execution method.
    /// </summary>
    public static async Task Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        var services = new ServiceCollection();
        ConfigureServices(services);

        using var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
        var manager = serviceProvider.GetRequiredService<IModelManager>();
        var engine = serviceProvider.GetRequiredService<IInferenceEngine>();

        logger.LogInformation("InstantAIGate CLI started.");

        try
        {
            var config = new ModelSettings
            {
                RepoId = "Qwen3VL-8B-Instruct-Q4_K_M",
                ContextSize = 4096,
                BatchSize = 512,
                GpuLayerCount = 99,
                MainGPU = 0,
                Threads = Environment.ProcessorCount,
                FlashAttention = true,
                KvCacheQuantization = "Q8",
                VisionSupport = false,
                MaxContexts = 2,
                Embeddings = false,
            };

            logger.LogInformation("Loading model: {RepoId}", config.RepoId);
            await manager.LoadModelAsync(config, CancellationToken.None);

            var messages = new[]
            {
                new ChatMessage("system", "You are a helpful AI assistant."),
                new ChatMessage("user", "Hi, What is the capital of France?")
            };

            // Apply native GGUF chat template dynamically
            var prompt = await engine.ApplyChatTemplateAsync(config.RepoId, messages, CancellationToken.None);
            logger.LogInformation("Formatted prompt:\n{Prompt}", prompt);

            var settings = new InferenceSettings
            {
                MaxTokens = 250,
                Temperature = 0.7f,
                TopP = 0.9f,
                TopK = 40,
                RepeatPenalty = 1.15f,
                PenaltyLastN = 64,
            };

            var responseBuilder = new StringBuilder();

            await foreach (var chunk in engine.StreamGenerationAsync(config.RepoId, prompt, settings, CancellationToken.None))
            {
                responseBuilder.Append(chunk);
                string currentText = responseBuilder.ToString();

                // Keep stop tokens to gracefully halt generation when the model finishes its answer
                if (currentText.Contains("<|im_end|>") || currentText.Contains("<|endoftext|>"))
                {
                    break;
                }

                Console.Write(chunk);
            }

            Console.WriteLine();
            logger.LogInformation("Generation completed. Total length: {Length} chars.", responseBuilder.Length);

            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Fatal error during CLI execution.");
        }
    }

    /// <summary>
    /// Configures dependency injection services.
    /// </summary>
    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        services.AddInstantAIGateInference();
        services.AddSingleton<IModelPathProvider>(new LocalModelPathProvider("C:\\models\\Qwen_Qwen3-VL-8B-Instruct-GGUF"));
    }
}