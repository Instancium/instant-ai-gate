using System.Runtime.InteropServices;

namespace InstantAIGate.Native.Bindings;

/// <summary>
/// Handles dynamic loading of native libraries for llama.cpp and mtmd.
/// </summary>
public static class NativeLibraryLoader
{
    private static readonly object LockObj = new();
    private static bool _isInitialized;
    private static IntPtr _llamaHandle;
    private static IntPtr _mtmdHandle;

    /// <summary>
    /// Gets a value indicating whether the native library is loaded.
    /// </summary>
    public static bool IsLoaded { get; private set; }

    /// <summary>
    /// Loads the native libraries from the specified path or default locations.
    /// </summary>
    /// <param name="libraryPath">Optional custom path to the native library directory.</param>
    /// <returns><see langword="true"/> if libraries were loaded successfully.</returns>
    /// <exception cref="InvalidOperationException">Thrown when libraries are already loaded.</exception>
    /// <exception cref="DllNotFoundException">Thrown when native libraries cannot be found.</exception>
    public static bool Load(string? libraryPath = null)
    {
        lock (LockObj)
        {
            if (_isInitialized)
            {
                return IsLoaded;
            }

            try
            {
                string platform = GetPlatformName();
                string extension = GetLibraryExtension();

                if (!string.IsNullOrEmpty(libraryPath))
                {
                    _llamaHandle = LoadNativeLibrary(Path.Combine(libraryPath, $"llama{extension}"));
                    _mtmdHandle = LoadNativeLibrary(Path.Combine(libraryPath, $"mtmd{extension}"));
                }
                else
                {
                    _llamaHandle = LoadNativeLibrary($"llama{extension}");
                    _mtmdHandle = LoadNativeLibrary($"mtmd{extension}");
                }

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
                throw new DllNotFoundException($"Failed to load native libraries. Platform: {GetPlatformName()}", ex);
            }
        }
    }

    /// <summary>
    /// Unloads the native libraries and frees backend resources.
    /// </summary>
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
                // Ignore errors during unload
            }
        }
    }

    private static string GetPlatformName()
    {
        if (OperatingSystem.IsWindows())
            return "win-x64";
        if (OperatingSystem.IsLinux())
            return "linux-x64";
        if (OperatingSystem.IsMacOS())
            return "osx-x64";
        return "unknown";
    }

    private static string GetLibraryExtension()
    {
        if (OperatingSystem.IsWindows())
            return ".dll";
        if (OperatingSystem.IsLinux())
            return ".so";
        if (OperatingSystem.IsMacOS())
            return ".dylib";
        return string.Empty;
    }

    private static IntPtr LoadNativeLibrary(string path)
    {
        if (NativeLibrary.TryLoad(path, out IntPtr handle))
        {
            return handle;
        }

        return IntPtr.Zero;
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
            // Ignore errors during backend free
        }
    }
}
