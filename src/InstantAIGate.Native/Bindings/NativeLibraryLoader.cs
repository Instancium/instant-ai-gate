namespace InstantAIGate.Native.Bindings;

using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

public static class NativeLibraryLoader
{
    private static readonly object LockObj = new();
    private static bool _isInitialized;

    // Track handles purely for unloading purposes if needed.
    private static IntPtr _llamaHandle;
    private static IntPtr _mtmdHandle;

    public static bool IsLoaded { get; private set; }

    public static bool Load(string? customRuntimesDirectory = null)
    {
        lock (LockObj)
        {
            if (_isInitialized)
            {
                return IsLoaded;
            }

            try
            {
                // Register the custom resolver for this assembly.
                NativeLibrary.SetDllImportResolver(typeof(NativeLibraryLoader).Assembly, CreateResolver(customRuntimesDirectory));

                // Force load to verify the bindings resolve correctly.
                _llamaHandle = NativeLibrary.Load("llama", typeof(NativeLibraryLoader).Assembly, null);
                _mtmdHandle = NativeLibrary.Load("mtmd", typeof(NativeLibraryLoader).Assembly, null);

                IsLoaded = _llamaHandle != IntPtr.Zero && _mtmdHandle != IntPtr.Zero;
                _isInitialized = true;

                if (IsLoaded)
                {
                    InitializeBackend();
                }

                return IsLoaded;
            }
            catch (Exception ex)
            {
                throw new DllNotFoundException($"Failed to load native libraries via DllImportResolver. Platform: {GetPlatformName()}", ex);
            }
        }
    }

    public static void Unload()
    {
        lock (LockObj)
        {
            if (!_isInitialized)
            {
                return;
            }

            try
            {
                FreeBackend();

                if (_llamaHandle != IntPtr.Zero)
                {
                    NativeLibrary.Free(_llamaHandle);
                    _llamaHandle = IntPtr.Zero;
                }

                if (_mtmdHandle != IntPtr.Zero)
                {
                    NativeLibrary.Free(_mtmdHandle);
                    _mtmdHandle = IntPtr.Zero;
                }

                IsLoaded = false;
                _isInitialized = false;
            }
            catch
            {
                // Swallow unload errors during teardown
            }
        }
    }

    private static DllImportResolver CreateResolver(string? customBasePath)
    {
        return (libraryName, assembly, searchPath) =>
        {
            // Only resolve our specific libraries
            if (libraryName != "llama" && libraryName != "mtmd")
            {
                return IntPtr.Zero;
            }

            string platform = GetPlatformName();
            string extension = GetLibraryExtension();
            string fileName = $"{libraryName}{extension}";

            // Priority 1: User-provided custom path
            if (!string.IsNullOrEmpty(customBasePath))
            {
                string customPath = Path.Combine(customBasePath, fileName);
                if (File.Exists(customPath) && NativeLibrary.TryLoad(customPath, out IntPtr handle))
                {
                    return handle;
                }
            }

            // Priority 2: Standard runtimes/{RID}/native layout
            string appBase = AppDomain.CurrentDomain.BaseDirectory;
            string runtimesPath = Path.Combine(appBase, "runtimes", platform, "native", fileName);

            if (File.Exists(runtimesPath) && NativeLibrary.TryLoad(runtimesPath, out IntPtr runtimesHandle))
            {
                return runtimesHandle;
            }

            // Priority 3: Root execution directory fallback
            string rootPath = Path.Combine(appBase, fileName);
            if (File.Exists(rootPath) && NativeLibrary.TryLoad(rootPath, out IntPtr rootHandle))
            {
                return rootHandle;
            }

            // Let the runtime fall back to standard OS resolution (e.g. PATH/LD_LIBRARY_PATH)
            return IntPtr.Zero;
        };
    }

    private static string GetPlatformName()
    {
        if (OperatingSystem.IsWindows()) return "win-x64";
        if (OperatingSystem.IsLinux()) return "linux-x64";
        if (OperatingSystem.IsMacOS()) return "osx-x64";
        return "unknown";
    }

    private static string GetLibraryExtension()
    {
        if (OperatingSystem.IsWindows()) return ".dll";
        if (OperatingSystem.IsLinux()) return ".so";
        if (OperatingSystem.IsMacOS()) return ".dylib";
        return string.Empty;
    }

    private static void InitializeBackend()
    {
        LlamaNative.llama_backend_init();
    }

    private static void FreeBackend()
    {
        try
        {
            LlamaNative.llama_backend_free();
        }
        catch
        {
            // Ignore during shutdown
        }
    }
}