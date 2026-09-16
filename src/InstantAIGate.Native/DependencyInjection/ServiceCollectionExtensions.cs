namespace InstantAIGate.Native.DependencyInjection;

using Microsoft.Extensions.DependencyInjection;
using InstantAIGate.Core.Interfaces.Inference;
using InstantAIGate.Core.Services.Inference;
using InstantAIGate.Native.Inference;

/// <summary>
/// Extension methods for registering InstantAIGate inference services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds InstantAIGate inference services to the specified service collection.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <returns>The service collection so that additional calls can be chained.</returns>
    public static IServiceCollection AddInstantAIGateInference(this IServiceCollection services)
    {
        // Register Native Facades (Singletons for stateful native resource management)
        services.AddSingleton<IBackendFacade, BackendFacade>();
        services.AddSingleton<IVisionFacade, VisionFacade>();

        // Register Core Services
        services.AddSingleton<RequestQueue>();
        services.AddSingleton<IModelProvider, ModelProvider>();
        services.AddSingleton<IModelManager, ModelManager>();

        // Register the high-level inference engine
        services.AddSingleton<IInferenceEngine, InferenceEngine>();

        // Note: IModelPathProvider is intentionally omitted here. 
        // It should be registered by the hosting application (CLI/Server) 
        // as its implementation depends on the specific environment 
        // (e.g., local file system, SSR registry, cloud storage).

        return services;
    }
}