namespace InstantAIGate.Native.DependencyInjection;

using InstantAIGate.Core.Interfaces.Inference;
using InstantAIGate.Core.Interfaces.Infrastructure;
using InstantAIGate.Core.Services.Inference;
using InstantAIGate.Core.Services.Infrastructure;
using InstantAIGate.Native.Inference;
using InstantAIGate.Native.Logging;
using InstantAIGate.Native.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Net.Http;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInstantAIGateInference(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IGatewayStateManager, GatewayStateManager>();
        services.AddSingleton<IBackendFacade, BackendFacade>();
        services.AddSingleton<IVisionFacade, VisionFacade>();
        services.AddSingleton<IModelLocator, LlamaModelLocator>();
        services.AddSingleton<IMetricsEventSource, MetricsEventSource>();
        services.AddSingleton<IQueueManager>(sp => new DynamicRequestQueue(
            initialLimit: 100,
            sp.GetService<TimeProvider>() ?? TimeProvider.System,
            sp.GetRequiredService<IMetricsEventSource>()));
        services.AddSingleton<IModelProvider, ModelProvider>();
        services.AddSingleton<IModelManager, ModelManager>();
        services.AddSingleton<IEmbeddingModelResolver, EmbeddingModelResolver>();
        services.AddSingleton<IInferenceEngine, LlamaInference>();
        services.AddSingleton<ISessionInferenceManager, SessionInferenceManager>();
        services.AddSingleton<HttpClient>();
        services.AddSingleton<IAssetManager, MemoryAssetManager>();
        services.AddSingleton<IMediaResolver, LocalTempMediaResolver>();
        services.AddSingleton<IVramProbe>(sp =>
            OperatingSystem.IsWindows()
                ? new WindowsVramProbe(sp.GetRequiredService<ILogger<WindowsVramProbe>>())
                : new LinuxVramProbe(sp.GetRequiredService<ILogger<LinuxVramProbe>>()));
        services.AddSingleton<IHardwareTelemetryProvider, SystemHardwareTelemetryProvider>();
        NativeStreamRedirector.Initialize(logMessage =>
        {
            Console.Out.WriteLine($"[ggml/clip] {logMessage}");
        });
        services.AddSingleton<IModelValidator, NativeModelValidator>();
        return services;
    }
}
