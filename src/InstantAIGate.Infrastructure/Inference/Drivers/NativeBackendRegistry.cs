
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Runtime.InteropServices;

namespace InstantAIGate.Infrastructure.Inference.Drivers
{


    /// <summary>
    /// Registry that discovers and manages available native backends.
    /// Automatically extracts compressed backend archives on first run before scanning.
    /// </summary>
    
    [Obsolete("Use LlamaDriverLoader and Driver-prefixed components. This class will be removed in a future release.", error: false)]
    public class NativeBackendRegistry : INativeBackendRegistry
    {
        private readonly ILogger<NativeBackendRegistry> _logger;
        private readonly NativeLibraryOptions _options;
        private readonly NativeRuntimeExtractor _extractor;
        private readonly object _lock = new();
        private List<NativeBackendInfo> _backends = new();
        private readonly string _currentRid;

        public NativeBackendRegistry(
            ILogger<NativeBackendRegistry> logger,
            IOptions<NativeLibraryOptions> options,
            NativeRuntimeExtractor extractor)
        {
            _logger = logger;
            _options = options.Value;
            _extractor = extractor;
            _currentRid = GetRuntimeIdentifier();
        }

        public IReadOnlyList<NativeBackendInfo> GetAllBackends()
        {
            lock (_lock) return _backends.AsReadOnly();
        }

        public IReadOnlyList<NativeBackendInfo> GetAvailableBackends()
        {
            lock (_lock) return _backends.Where(b => b.IsAvailable).ToList().AsReadOnly();
        }

        public NativeBackendInfo? GetBackend(string name)
        {
            lock (_lock)
            {
                return _backends.FirstOrDefault(b =>
                    b.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            }
        }

        public NativeBackendInfo ResolveBackend(string preferredBackend)
        {
            lock (_lock)
            {
                var available = _backends.Where(b => b.IsAvailable).ToList();

                if (available.Count == 0)
                {
                    throw new InvalidOperationException(
                        $"No native backends found for '{_currentRid}'. " +
                        "Ensure the '.runtimes' folder exists in the solution root or application directory.");
                }

                if (preferredBackend.Equals("auto", StringComparison.OrdinalIgnoreCase))
                {
                    var gpu = available.FirstOrDefault(b => b.IsGpu);
                    if (gpu != null)
                    {
                        _logger.LogInformation("Auto-selected GPU backend: {Backend}", gpu.Name);
                        return gpu;
                    }

                    var cpu = available.FirstOrDefault(b => !b.IsGpu);
                    if (cpu != null)
                    {
                        _logger.LogInformation("Auto-selected CPU backend (no GPU available)");
                        return cpu;
                    }
                }

                var requested = available.FirstOrDefault(b =>
                    b.Name.Equals(preferredBackend, StringComparison.OrdinalIgnoreCase));

                if (requested == null)
                {
                    var availableNames = string.Join(", ", available.Select(b => b.Name));
                    throw new InvalidOperationException(
                        $"Requested backend '{preferredBackend}' is not available. " +
                        $"Available backends for {_currentRid}: {availableNames}");
                }

                _logger.LogInformation("Selected backend: {Backend}", requested.Name);
                return requested;
            }
        }

        public void Refresh()
        {
            lock (_lock)
            {
                _backends.Clear();

                var basePath = AppContext.BaseDirectory;
                var nativePath = Path.Combine(basePath, "runtimes", _currentRid);

                // Extract compressed archives before scanning (no-op if already extracted)
                _extractor.EnsureExtracted(nativePath);

                _logger.LogInformation("Scanning for native backends at: {Path}", nativePath);

                if (!Directory.Exists(nativePath))
                {
                    _logger.LogWarning("Native backends directory not found: {Path}", nativePath);
                    return;
                }

                foreach (var backendDir in Directory.GetDirectories(nativePath))
                {
                    var backendName = Path.GetFileName(backendDir);
                    var libraries = Directory.GetFiles(backendDir, "*", SearchOption.AllDirectories)
                        .Where(f => IsNativeLibrary(f))
                        .Select(f => Path.GetRelativePath(backendDir, f))
                        .ToList();

                    var hasLlama = libraries.Any(l =>
                        string.Equals(Path.GetFileName(l), "llama.dll", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(Path.GetFileName(l), "libllama.so", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(Path.GetFileName(l), "libllama.dylib", StringComparison.OrdinalIgnoreCase));

                    _backends.Add(new NativeBackendInfo
                    {
                        Rid = _currentRid,
                        Name = backendName,
                        Path = backendDir,
                        IsAvailable = hasLlama && libraries.Count > 0,
                        LibraryFiles = libraries!
                    });

                    _logger.LogDebug(
                        "Discovered backend: {Name}, Available: {Available}, Libraries: {Libraries}",
                        backendName, hasLlama, string.Join(", ", libraries));
                }

                _logger.LogInformation(
                    "Backend registry refreshed for {Rid}. Found {Total} backends, {Available} available",
                    _currentRid, _backends.Count, _backends.Count(b => b.IsAvailable));
            }
        }

        private static string GetRuntimeIdentifier()
        {
            var arch = RuntimeInformation.OSArchitecture switch
            {
                Architecture.X64 => "x64",
                Architecture.Arm64 => "arm64",
                _ => throw new PlatformNotSupportedException($"Unsupported architecture: {RuntimeInformation.OSArchitecture}")
            };

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return $"win-{arch}";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return $"linux-{arch}";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return $"osx-{arch}";

            throw new PlatformNotSupportedException($"Unsupported OS: {RuntimeInformation.OSDescription}");
        }

        private static bool IsNativeLibrary(string filePath)
        {
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            return ext is ".dll" or ".so" or ".dylib";
        }
    }

}
