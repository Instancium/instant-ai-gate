using InstantAIGate.Infrastructure.Adapters;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

public static class Program
{
    public static async Task Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;

        string modelDirectory = @"C:\models\Qwen3-VL-4B-Instruct-ONNX\onnxruntime\cuda\cuda-int4-rtn-block-32";
        string testImagePath = @"C:\models\test-4.png";

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("Instantiating adapter...");
        Console.ResetColor();

        using var adapter = new MultiModalAdapter();

        try
        {
            adapter.Initialize(modelDirectory, "cuda");

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Initialization successful. Model loaded into memory.");
            Console.ResetColor();

            var images = new List<string> { testImagePath };
            var audios = new List<string>();

            using var cancellationTokenSource = new CancellationTokenSource();

            Console.CancelKeyPress += (sender, eventArgs) =>
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("\nCancellation requested by user...");
                Console.ResetColor();
                cancellationTokenSource.Cancel();
                eventArgs.Cancel = true;
            };

            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine("\n🤖 AI: I see the delivery note. What should I do?");
            Console.ResetColor();

            while (true)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write("\n👤 User: ");
                Console.ResetColor();

                string prompt = Console.ReadLine() + " Do not use Markdown formatting.";

                if (string.IsNullOrWhiteSpace(prompt))
                {
                    continue;
                }

                if (prompt.Trim().ToLower() == "exit")
                {
                    break;
                }

                Console.ForegroundColor = ConsoleColor.Blue;
                Console.Write("\n🤖 AI: ");
                Console.ResetColor();

                var tokenStream = adapter.GenerateStreamAsync(
                    prompt,
                    images,
                    audios,
                    maxLength: 8192,
                    cancellationToken: cancellationTokenSource.Token
                );

                await foreach (var token in tokenStream)
                {
                    Console.Write(token);
                }

                Console.WriteLine();
            }
        }
        catch (OperationCanceledException)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("\nGeneration was cancelled.");
            Console.ResetColor();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\nAn error occurred during execution: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            Console.ResetColor();
        }
    }
}