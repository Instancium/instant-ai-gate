using InstantAIGate.Runners.Onnx.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Text;

namespace InstantAIGate.Cli
{
    public static class Program
    {
        public static async Task Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.InputEncoding = Encoding.UTF8;

            var host = Host.CreateDefaultBuilder(args)
                .ConfigureServices((context, services) =>
                {
                    services.AddOnnxRunner();

                    services.AddHostedService<CliHostedService>();
                })
                .Build();

            await host.RunAsync();
        }
    }
}