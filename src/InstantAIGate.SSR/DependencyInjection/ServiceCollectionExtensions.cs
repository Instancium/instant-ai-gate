namespace InstantAIGate.SSR.DependencyInjection;

using InstantAIGate.SSR.Contracts;
using InstantAIGate.SSR.Downloader;
using InstantAIGate.SSR.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInstantAIGateSSR(this IServiceCollection services)
    {
        // Configure HttpClientHandler to strictly follow redirects (HTTP 301/302)
        services.AddHttpClient<IModelDownloader, ParallelModelDownloader>()
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 5
            })
            .ConfigureHttpClient(client =>
            {
                client.Timeout = System.TimeSpan.FromHours(1);

                // Spoof User-Agent to bypass basic Cloudflare/CDN bot protections
                client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36 InstantAIGate/2.0");
                client.DefaultRequestHeaders.Add("Accept", "*/*");
            });

        services.AddSingleton<IModelCatalogService, ModelCatalogService>();

        return services;
    }
}