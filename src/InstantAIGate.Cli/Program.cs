namespace InstantAIGate.Cli;

using InstantAIGate.Cli.Services;
using InstantAIGate.Core.Dtos.Config;
using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Interfaces.Inference;
using InstantAIGate.Native.DependencyInjection;
using InstantAIGate.Native.Inference;
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
    /// Формирует правильный ChatML формат напрямую в C#, игнорируя сломанные метаданные модели.
    /// </summary>
    public static string BuildChatML(IEnumerable<Core.Dtos.Inference.ChatMessage> messages)
    {
        if (messages == null) return string.Empty;

        var sb = new System.Text.StringBuilder();
        foreach (var msg in messages)
        {
            // Оборачиваем каждое сообщение в теги Qwen
            sb.Append($"<|im_start|>{msg.Role}\n{msg.Content}<|im_end|>\n");
        }

        // Добавляем маркер того, что теперь очередь ассистента генерировать ответ
        sb.Append("<|im_start|>assistant\n");

        return sb.ToString();
    }

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

            //string prompt = "{% for message in messages %}{{'<|im_start|>' + message['role'] + '\n' + message['content'] + '<|im_end|>' + '\n'}}{% endfor %}{% if add_generation_prompt %}{{ '<|im_start|>assistant\n' }}{% endif %}"; 
            //var prompt = "<|im_start|>What is the capital of France? Answer in one word.<|im_end|>\n<|im_start|>assistant\n";
            var prompt = BuildChatML(messages);
            logger.LogInformation("Formatted prompt:\n{Prompt}", prompt);

            var settings = new InferenceSettings
            {
                MaxTokens = 250,      // Дадим ей чуть больше места на случай длинных ответов
                Temperature = 0.7f,   // Понижаем температуру (0.4) для точных ответов без фантазий
                TopP = 0.9f,
                TopK = 40,

                // Идеальный баланс для Qwen:
                RepeatPenalty = 1.15f, // Чуть-чуть штрафуем повторения, чтобы убить "????"
                PenaltyLastN = 64,     // Окно памяти в 64 токена (достаточно от зацикливаний)
            };

            var responseBuilder = new StringBuilder();
      
            await foreach (var chunk in engine.StreamGenerationAsync(config.RepoId, prompt, settings, CancellationToken.None))
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