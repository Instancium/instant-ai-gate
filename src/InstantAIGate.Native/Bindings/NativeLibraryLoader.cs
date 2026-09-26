namespace InstantAIGate.Native.Bindings;

using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

public static class NativeLibraryLoader
{
    private static readonly object LockObj = new();
    private static bool _isInitialized;

    private static IntPtr _llamaHandle;
    private static IntPtr _mtmdHandle;

    public static bool IsLoaded { get; private set; }

    //public static bool Load(string? customRuntimesDirectory = null)
    //{
    //    lock (LockObj)
    //    {
    //        if (_isInitialized)
    //        {
    //            return IsLoaded;
    //        }

    //        try
    //        {
    //            NativeLibrary.SetDllImportResolver(typeof(NativeLibraryLoader).Assembly,
    //                (libName, asm, searchPath) => DllResolver(libName, asm, searchPath, customRuntimesDirectory));

    //            _llamaHandle = NativeLibrary.Load("llama", typeof(NativeLibraryLoader).Assembly, null);
    //            _mtmdHandle = NativeLibrary.Load("mtmd", typeof(NativeLibraryLoader).Assembly, null);

    //            IsLoaded = _llamaHandle != IntPtr.Zero && _mtmdHandle != IntPtr.Zero;
    //            _isInitialized = true;

    //            if (IsLoaded)
    //            {
    //                InitializeBackend();
    //            }

    //            return IsLoaded;
    //        }
    //        catch (Exception ex)
    //        {
    //            throw new DllNotFoundException($"Failed to load native libraries. Platform: {GetPlatformName()}", ex);
    //        }
    //    }
    //}

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
                // SetDllImportResolver can only be called once per assembly.
                // We catch InvalidOperationException if it was previously set (e.g. across test teardowns).
                try
                {
                    NativeLibrary.SetDllImportResolver(typeof(NativeLibraryLoader).Assembly,
                        (libName, asm, searchPath) => DllResolver(libName, asm, searchPath, customRuntimesDirectory));
                }
                catch (InvalidOperationException)
                {
                    // Resolver is already registered, which is safe to ignore during test reruns.
                }

                if (_llamaHandle == IntPtr.Zero)
                {
                    _llamaHandle = NativeLibrary.Load("llama", typeof(NativeLibraryLoader).Assembly, null);
                }

                if (_mtmdHandle == IntPtr.Zero)
                {
                    _mtmdHandle = NativeLibrary.Load("mtmd", typeof(NativeLibraryLoader).Assembly, null);
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

    public static void Unload()
    {
        lock (LockObj)
        {
            if (!_isInitialized) return;

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
                // Swallow unload exceptions during teardown
            }
        }
    }

    private static IntPtr DllResolver(string libraryName, Assembly assembly, DllImportSearchPath? searchPath, string? customBasePath)
    {
        if (libraryName != "llama" && libraryName != "mtmd")
        {
            return IntPtr.Zero;
        }

        string extension = GetLibraryExtension();
        string fileName = $"{libraryName}{extension}";

        if (!string.IsNullOrEmpty(customBasePath))
        {
            string customPath = Path.Combine(customBasePath, fileName);
            if (File.Exists(customPath) && NativeLibrary.TryLoad(customPath, assembly, DllImportSearchPath.UseDllDirectoryForDependencies, out IntPtr customHandle))
            {
                return customHandle;
            }
        }

        string appBase = AppDomain.CurrentDomain.BaseDirectory;
        string rootPath = Path.Combine(appBase, fileName);

        if (File.Exists(rootPath) && NativeLibrary.TryLoad(rootPath, out IntPtr rootHandle))
        {
            return rootHandle;
        }

        return IntPtr.Zero;
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
        }
    }
}