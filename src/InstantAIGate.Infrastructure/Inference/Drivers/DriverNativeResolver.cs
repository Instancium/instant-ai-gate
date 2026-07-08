using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace InstantAIGate.Infrastructure.Inference.Drivers;

/// <summary>
/// Registers a custom P/Invoke resolver to locate native libraries in non-standard fallback paths (e.g., Temp).
/// </summary>
internal static class DriverNativeResolver
{
    private static string? _driverDirectory;
    private static bool _isRegistered;
    private static readonly object _registrationLock = new();

    /// <summary>
    /// Registers the specified directory as the primary search path for native llama libraries.
    /// </summary>
    public static void Register(string driverDirectory)
    {
        lock (_registrationLock)
        {
            if (_isRegistered)
            {
                return;
            }

            _driverDirectory = driverDirectory;
            NativeLibrary.SetDllImportResolver(typeof(DriverNativeResolver).Assembly, DllImportResolver);
            _isRegistered = true;
        }
    }

    private static IntPtr DllImportResolver(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (string.IsNullOrWhiteSpace(_driverDirectory))
        {
            return IntPtr.Zero;
        }

        // Intercept only llama-related requests
        if (libraryName.Contains("llama", StringComparison.OrdinalIgnoreCase) ||
            libraryName.Contains("ggml", StringComparison.OrdinalIgnoreCase))
        {
            var expectedLibName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? (libraryName.EndsWith(".dll") ? libraryName : $"{libraryName}.dll")
                : (libraryName.StartsWith("lib") ? $"{libraryName}.so" : $"lib{libraryName}.so");

            var absolutePath = Path.Combine(_driverDirectory, expectedLibName);

            if (File.Exists(absolutePath) && NativeLibrary.TryLoad(absolutePath, out var handle))
            {
                return handle;
            }
        }

        // Fallback to default .NET Core resolving logic
        return IntPtr.Zero;
    }
}