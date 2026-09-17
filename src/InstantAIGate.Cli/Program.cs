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

public class Program
{
    public static async Task Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;

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
                VisionSupport = true,
                MaxContexts = 2,
                Embeddings = false,
            };

            logger.LogInformation("Loading model: {RepoId}", config.RepoId);
            await manager.LoadModelAsync(config, CancellationToken.None);


            var imagePaths = new[] { "C:\\models\\test-1.jpeg" };

            var messages = new[]
            {
                new ChatMessage("system", "You are a helpful AI assistant."),
                new ChatMessage("user", "<__media__>\nDescribe in detail what is depicted in this picture.")
            };

            var prompt = await engine.ApplyChatTemplateAsync(config.RepoId, messages, CancellationToken.None);
            logger.LogInformation("Formatted prompt:\n{Prompt}", prompt);

            var settings = new InferenceSettings
            {
                MaxTokens = 1024,
                Temperature = 0.7f,
                TopP = 0.9f,
                TopK = 40,
            };

            var responseBuilder = new StringBuilder();

      
            await foreach (var chunk in engine.StreamGenerationAsync(config.RepoId, prompt, imagePaths, settings, CancellationToken.None))
            {
                responseBuilder.Append(chunk);
                string currentText = responseBuilder.ToString();

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