using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

namespace InstantAIGate.Infrastructure.Inference.Drivers;

internal static class DriverNativeResolver
{
    private static string? _driverDirectory;
    private static bool _isAttached;
    private static bool _isPathRegistered;
    private static readonly object _registrationLock = new();


    private static readonly ManualResetEventSlim _extractionWaitHandle = new(false);

    public static void Attach()
    {
        lock (_registrationLock)
        {
            if (_isAttached) return;
            NativeLibrary.SetDllImportResolver(typeof(DriverNativeResolver).Assembly, DllImportResolver);
            _isAttached = true;
        }
    }

    public static void RegisterPath(string driverDirectory)
    {
        lock (_registrationLock)
        {
            _driverDirectory = driverDirectory;
            _isPathRegistered = true;
            _extractionWaitHandle.Set(); 
        }
    }

    private static IntPtr DllImportResolver(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName.Contains("llama", StringComparison.OrdinalIgnoreCase) ||
            libraryName.Contains("ggml", StringComparison.OrdinalIgnoreCase))
        {

            if (!_isPathRegistered)
            {
                Console.WriteLine($"[DriverNativeResolver] API is trying to load '{libraryName}' early. Blocking thread until extraction finishes...");
                _extractionWaitHandle.Wait(TimeSpan.FromSeconds(60));
            }

            if (string.IsNullOrWhiteSpace(_driverDirectory)) return IntPtr.Zero;

            var expectedLibName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? (libraryName.EndsWith(".dll") ? libraryName : $"{libraryName}.dll")
                : (libraryName.StartsWith("lib") ? $"{libraryName}.so" : $"lib{libraryName}.so");

            var absolutePath = Path.Combine(_driverDirectory, expectedLibName);

            if (File.Exists(absolutePath))
            {
                try
                {
                    return NativeLibrary.Load(absolutePath);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[CRITICAL] OS refused to load {absolutePath}. Real reason: {ex.Message}");
                    throw;
                }
            }
        }
        return IntPtr.Zero;
    }
}