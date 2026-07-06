using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Runtime.InteropServices;

namespace InstantAIGate.Infrastructure.Inference.Drivers
{

    /// <summary>
    /// Loads native libraries into the current process.
    /// Copies DLLs from ALL available backends to the application root,
    /// allowing llama.cpp to use CPU fallback when GPU memory is insufficient.
    /// </summary>
    public class NativeLibraryLoader : INativeLibraryLoader
    {
        private readonly ILogger<NativeLibraryLoader> _logger;
        private readonly INativeBackendRegistry _backendRegistry;
        private readonly NativeLibraryOptions _options;
        private readonly object _lock = new();
        private IntPtr _llamaHandle = IntPtr.Zero;
        private NativeBackendInfo? _currentBackend;

        public bool IsLoaded => _llamaHandle != IntPtr.Zero;
        public NativeBackendInfo? CurrentBackend => _currentBackend;

        public NativeLibraryLoader(
            ILogger<NativeLibraryLoader> logger,
            INativeBackendRegistry backendRegistry,
            IOptions<NativeLibraryOptions> options)
        {
            _logger = logger;
            _backendRegistry = backendRegistry;
            _options = options.Value;
        }

        public void LoadBackend(NativeBackendInfo backend)
        {
            if (IsLoaded)
            {
                if (_options.EnableDebugLogging)
                    _logger.LogDebug("Native library already loaded from: {Path}", _currentBackend?.Path);
                return;
            }

            lock (_lock)
            {
                if (IsLoaded) return;

                if (!backend.IsAvailable)
                    throw new InvalidOperationException($"Cannot load backend '{backend.Name}': not available.");

                _logger.LogInformation("Loading native backend: {Backend} from {Path}", backend.Name, backend.Path);

                // Copy DLLs from ALL available backends (CPU + GPU) to app root.
                // This allows llama.cpp to use CPU memory as fallback when GPU is full.
                CopyAllBackendsToRoot(backend);

                // Load the main llama library from the selected backend
                var libName = GetLlamaLibraryName();
                var fullPath = Path.Combine(backend.Path, libName);

                if (!File.Exists(fullPath))
                    throw new FileNotFoundException($"Native library not found: {fullPath}");

                _logger.LogInformation("Loading native library: {Path}", fullPath);

                if (!NativeLibrary.TryLoad(fullPath, out _llamaHandle))
                    throw new DllNotFoundException($"Failed to load native library: {fullPath}");

                _currentBackend = backend;

                _logger.LogInformation(
                    "Native library loaded successfully. Backend: {Backend}, Handle: {Handle}",
                    backend.Name, _llamaHandle);
            }
        }

        /// <summary>
        /// Copies native libraries from ALL available backends to the application root.
        /// The selected backend's DLLs are copied LAST to ensure they take priority
        /// when filenames overlap (e.g., llama.dll exists in both cpu/ and vulkan/).
        /// </summary>
        private void CopyAllBackendsToRoot(NativeBackendInfo selectedBackend)
        {
            var destPath = AppContext.BaseDirectory;
            var allBackends = _backendRegistry.GetAvailableBackends();

            if (_options.EnableDebugLogging)
                _logger.LogDebug("Copying DLLs from {Count} available backends to {Dest}", allBackends.Count, destPath);

            // 1. Сначала копируем все невыбранные бэкенды (например, CPU)
            foreach (var b in allBackends.Where(b => b.Name != selectedBackend.Name))
            {
                CopyBackendLibraries(b, destPath);
            }

            // 2. В самом конце копируем выбранный бэкенд (CUDA), чтобы он перезаписал файлы CPU!
            CopyBackendLibraries(selectedBackend, destPath);
        }

        private void CopyBackendLibraries(NativeBackendInfo backend, string destPath)
        {
            if (_options.EnableDebugLogging)
                _logger.LogDebug("Copying libraries from backend '{Backend}': {Path}", backend.Name, backend.Path);

            foreach (var libFile in backend.LibraryFiles)
            {
                var sourceFile = Path.Combine(backend.Path, libFile);
                var destFile = Path.Combine(destPath, libFile);

                try
                {
                    var destDir = Path.GetDirectoryName(destFile);
                    if (!string.IsNullOrEmpty(destDir)) Directory.CreateDirectory(destDir);
                    File.Copy(sourceFile, destFile, overwrite: true);
                    if (_options.EnableDebugLogging)
                        _logger.LogDebug("  Copied: {File} (from {Backend})", libFile, backend.Name);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to copy native library: {File}", libFile);
                }
            }
        }

        private static string GetLlamaLibraryName()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return "llama.dll";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return "libllama.so";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return "libllama.dylib";
            throw new PlatformNotSupportedException($"Unsupported OS: {RuntimeInformation.OSDescription}");
        }
    }
}
