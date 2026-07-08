using System;
using System.IO;
using System.Runtime.InteropServices;

namespace InstantAIGate.Infrastructure.Inference.Drivers;

/// <summary>
/// Detects the current runtime environment, architecture, and available hardware acceleration.
/// </summary>
public static class DriverEnvironmentDetector
{
    private const string RuntimesPathEnvironmentVariable = "INSTANTAI_RUNTIMES_PATH";
    private const string WindowsCudaLibrary = "nvcuda.dll";
    private const string LinuxCudaLibrary = "libcuda.so.1";

    /// <summary>
    /// Validates that the current execution platform is supported.
    /// </summary>
    /// <exception cref="PlatformNotSupportedException">Thrown when the architecture is not x64 or OS is unsupported.</exception>
    public static void EnsureCompatiblePlatform()
    {
        if (RuntimeInformation.ProcessArchitecture != Architecture.X64)
        {
            throw new PlatformNotSupportedException($"Unsupported architecture: {RuntimeInformation.ProcessArchitecture}. Required: x64.");
        }

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && !RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            throw new PlatformNotSupportedException($"Unsupported OS: {RuntimeInformation.OSDescription}. Required: Windows or Linux.");
        }
    }

    /// <summary>
    /// Retrieves the local runtimes path if configured for debug mode.
    /// </summary>
    /// <returns>The physical path to the local runtimes, or null if running in production mode.</returns>
    public static string? GetLocalRuntimesPath()
    {
        var path = Environment.GetEnvironmentVariable(RuntimesPathEnvironmentVariable);
        return !string.IsNullOrWhiteSpace(path) && Directory.Exists(path) ? path : null;
    }

    /// <summary>
    /// Determines if the system has compatible CUDA drivers installed.
    /// </summary>
    /// <returns>True if CUDA is available; otherwise, false.</returns>
    public static bool IsCudaAvailable()
    {
        var libraryName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? WindowsCudaLibrary : LinuxCudaLibrary;

        // Validates physical driver presence via OS loader without touching llama.cpp
        if (NativeLibrary.TryLoad(libraryName, typeof(DriverEnvironmentDetector).Assembly, DllImportSearchPath.System32 | DllImportSearchPath.ApplicationDirectory, out var handle))
        {
            NativeLibrary.Free(handle);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Gets the standardized OS prefix for the current platform.
    /// </summary>
    /// <returns>"Windows" or "Linux".</returns>
    public static string GetCurrentOsPrefix() => RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Windows" : "Linux";
}