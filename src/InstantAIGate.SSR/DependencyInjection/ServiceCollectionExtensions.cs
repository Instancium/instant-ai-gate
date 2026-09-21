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
        // Register HTTP Client for Downloader with basic resilience
        services.AddHttpClient<IModelDownloader, ParallelModelDownloader>(client =>
        {
            // You can configure default headers or timeouts here if needed
            client.Timeout = System.TimeSpan.FromHours(1); // Models can be large
        });

        // Register Catalog Service as Singleton (caches the parsed JSON)
        services.AddSingleton<IModelCatalogService, ModelCatalogService>();

        return services;
    }
}