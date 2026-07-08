using InstantAIGate.Application.Interfaces;
using InstantAIGate.Application.Interfaces.Catalog;
using InstantAIGate.Application.Interfaces.Inference;
using InstantAIGate.Application.Interfaces.Storage;
using InstantAIGate.Infrastructure.Catalog;
using InstantAIGate.Infrastructure.Inference;
using InstantAIGate.Infrastructure.Inference.Adapters;
using InstantAIGate.Infrastructure.Inference.Native;
using InstantAIGate.Infrastructure.NvmlNative;
using InstantAIGate.Infrastructure.Services;
using InstantAIGate.Infrastructure.Storage;
using InstantAIGate.Infrastructure.Telemetry;
using Microsoft.Extensions.Configuration;
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

            // --- Core LLamaSharp Native Infrastructure ---
            // Holds raw native model weight references and manages low-level context recycling pools
            services.AddSingleton<INativeLlamaApi, NativeLlamaApi>();
            services.AddSingleton<IModelProvider, LlamaModelProvider>();

            // --- Multi-Model Lifecycle Orchestrator ---
            // Manages physical VRAM/RAM slot assignments, handles explicit unloading, and drives user concurrency throttling
            services.AddSingleton<LlamaModelManager>();
            services.AddSingleton<IModelManager>(sp => sp.GetRequiredService<LlamaModelManager>());
            services.AddSingleton<ILlamaModelManager>(sp => sp.GetRequiredService<LlamaModelManager>());

            // --- Text Inference Adapters ---
            // Stateless high-level execution layer responsible for token-streaming and complete string generation blocks
            services.AddTransient<IChatAdapter, LlamaChatAdapter>();
            services.AddTransient<IEmbeddingAdapter, LlamaEmbeddingAdapter>();

            // --- Remote Storage and File Management Services ---
            services.AddSingleton<IHttpDownloader, HttpDownloader>();
            services.AddSingleton<IFileStorageService, FileStorageService>();
            services.AddSingleton<IModelStorageService, HttpModelStorageService>();
            services.AddSingleton<IModelStorageChecker, ModelStorageChecker>();

            // --- Telemetry ---
            services.AddSingleton<NvmlProvider>();
            services.AddSingleton<ITelemetryService, TelemetryService>();

            return services;
        }
    }
}