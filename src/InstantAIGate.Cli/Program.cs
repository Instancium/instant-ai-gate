using InstantAIGate.Infrastructure.Adapters;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public static class Program
{
    public static async Task Main(string[] args)
    {
        string modelDirectory = @"C:\models\Qwen3-VL-4B-Instruct-ONNX\onnxruntime\cpu_and_mobile\cpu-int4-rtn-block-32";
        string testImagePath = @"C:\models\test.jpg";

        // ВАЖНО: передаем ТОЛЬКО чистый текст. Адаптер сам вставит теги и JSON-обертку!
        string prompt = "Please describe what you see in this image in detail.";

        Console.WriteLine("Instantiating adapter...");
        using var adapter = new MultiModalAdapter();

        try
        {
            Console.WriteLine($"Initializing model from: {modelDirectory}");
            adapter.Initialize(modelDirectory, "cpu");
            Console.WriteLine("Initialization successful. Model loaded into memory.");

            var images = new List<string> { testImagePath };
            var audios = new List<string>();

            using var cancellationTokenSource = new CancellationTokenSource();

            Console.CancelKeyPress += (sender, eventArgs) =>
            {
                Console.WriteLine("\nCancellation requested by user...");
                cancellationTokenSource.Cancel();
                eventArgs.Cancel = true;
            };

            Console.WriteLine("Starting inference stream...\n");
            Console.Write("AI: ");

            var tokenStream = adapter.GenerateStreamAsync(
                prompt,
                images,
                audios,
                maxLength: 1024,
                cancellationToken: cancellationTokenSource.Token
            );

            await foreach (var token in tokenStream)
            {
                Console.Write(token);
            }

            Console.WriteLine("\n\nStream completed successfully.");
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("\nGeneration was cancelled.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\nAn error occurred during execution: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
    }
}