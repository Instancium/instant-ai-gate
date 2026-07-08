using System;

namespace InstantAIGate.Infrastructure.Inference.Drivers;

/// <summary>
/// Orchestrates the initialization, extraction, and loading of native llama.cpp drivers.
/// </summary>
public static class LlamaDriverLoader
{
    private static readonly object SynchronizationLock = new();
    private static bool isInitialized;

    /// <summary>
    /// Ensures that all required native drivers are extracted and ready for use.
    /// </summary>
    public static void EnsureInitialized()
    {
        if (isInitialized)
        {
            return;
        }

        lock (SynchronizationLock)
        {
            if (isInitialized)
            {
                return;
            }

            DriverEnvironmentDetector.EnsureCompatiblePlatform();

            var useCuda = DriverEnvironmentDetector.IsCudaAvailable();
            var osPrefix = DriverEnvironmentDetector.GetCurrentOsPrefix();
            var localPath = DriverEnvironmentDetector.GetLocalRuntimesPath();

            // TODO: Zone C extraction and resolver registration
            // var finalDriverPath = DriverExtractor.ExtractAndGetPath(osPrefix, useCuda, localPath);
            // DriverNativeResolver.Register(finalDriverPath);

            isInitialized = true;
        }
    }
}