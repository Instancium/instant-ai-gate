using System;

namespace InstantAIGate.Infrastructure.Inference.Drivers;

public static class LlamaDriverLoader
{
    private static readonly object SynchronizationLock = new();
    private static bool isInitialized;

    public static void EnsureInitialized()
    {
        if (isInitialized) return;

        lock (SynchronizationLock)
        {
            if (isInitialized) return;

            DriverEnvironmentDetector.EnsureCompatiblePlatform();

            var useCuda = DriverEnvironmentDetector.IsCudaAvailable();
            var osPrefix = DriverEnvironmentDetector.GetCurrentOsPrefix();
            var localPath = DriverEnvironmentDetector.GetLocalRuntimesPath();

            var finalDriverPath = DriverExtractor.ExtractAndGetPath(osPrefix, useCuda, localPath);

       
            DriverNativeResolver.RegisterPath(finalDriverPath);

            isInitialized = true;
        }
    }
}