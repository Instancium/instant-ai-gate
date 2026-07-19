using InstantAIGate.Application.Interfaces;
using InstantAIGate.Application.Interfaces.Catalog;
using InstantAIGate.Application.Interfaces.Inference;
using InstantAIGate.Application.Interfaces.Storage;
using InstantAIGate.Infrastructure.Catalog;
using InstantAIGate.Infrastructure.Inference;
using InstantAIGate.Infrastructure.Inference.Adapters;
using InstantAIGate.Infrastructure.Inference.Drivers;
using InstantAIGate.Infrastructure.Inference.Native;
using InstantAIGate.Infrastructure.NvmlNative;
using InstantAIGate.Infrastructure.Storage;
using InstantAIGate.Infrastructure.Telemetry;
using Microsoft.Extensions.DependencyInjection;

namespace InstantAIGate.Infrastructure
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddInstantAIGateInfrastructure(this IServiceCollection services, Action<StorageOptions> configureOptions)
        {
            services.Configure(configureOptions);
            return services.RegisterCoreServices();
        }

        private static IServiceCollection RegisterCoreServices(this IServiceCollection services)
        {
            services.AddSingleton<IModelPathProvider, ModelPathProvider>();

            // --- Model Registry Tracking ---
            services.AddSingleton<IModelRegistry, InMemoryModelRegistry>();

            // Holds raw native model weight references and manages low-level context recycling pools
            services.AddSingleton<NativeLlamaApi>();
            services.AddSingleton<ModelProvider>();

            // Manages physical VRAM/RAM slot assignments, handles explicit unloading, and drives user concurrency throttling
            services.AddSingleton<ModelManager>();
            services.AddSingleton<IModelManager>(sp => sp.GetRequiredService<ModelManager>());

            services.AddTransient<IChatAdapter, ChatAdapter>(); 

            services.AddTransient<IEmbeddingAdapter, EmbeddingAdapter>();

            // --- Remote Storage and File Management Services ---
            services.AddSingleton<IHttpDownloader, HttpDownloader>();
            services.AddSingleton<IFileStorageService, FileStorageService>();
            services.AddSingleton<IModelStorageService, HttpModelStorageService>();
            services.AddSingleton<IModelStorageChecker, ModelStorageChecker>();

            // --- Telemetry ---
            services.AddSingleton<NvmlProvider>();

            services.AddSingleton<IDriverStateProvider, DriverStateProvider>();
            services.AddHostedService<DriverInitializationHostedService>();
            services.AddSingleton<ITelemetryService, TelemetryService>();


            return services;
        }
    }
}