using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using SharpCompress.Archives.SevenZip;

namespace InstantAIGate.Infrastructure.Inference.Drivers;

/// <summary>
/// Handles the extraction of native runtime archives with cross-process synchronization.
/// </summary>
internal static class DriverExtractor
{
    private const string MutexName = "Global\\InstantAIGate_Extract_Mutex";
    private const string MarkerFileName = ".instantaigate-version";
    private const string DefaultTempFolderName = "InstantAIGate";

    /// <summary>
    /// Extracts the required drivers if necessary and returns the physical path to the binaries.
    /// </summary>
    public static string ExtractAndGetPath(string osPrefix, bool useCuda, string? localPath)
    {
        // 1. Local Debug Override (Bypasses extraction completely)
        if (!string.IsNullOrWhiteSpace(localPath))
        {
            // Maps to: C:\Instancium\source\InstantAIGate\.runtimes\Windows\x64
            var debugDriverPath = Path.Combine(localPath, osPrefix, "x64");

            if (!Directory.Exists(debugDriverPath))
            {
                throw new DirectoryNotFoundException(
                    $"Local debug environment detected, but the required architecture path was not found: {debugDriverPath}");
            }

            return debugDriverPath;
        }

        var version = typeof(DriverExtractor).Assembly.GetName().Version?.ToString() ?? "1.0.0";
        var baseTargetDirectory = AppContext.BaseDirectory;

        // 2. Cross-process synchronization (Production Mode)
        using var mutex = new Mutex(false, MutexName);
        try
        {
            mutex.WaitOne();

            // 3. Fallback routing (BaseDirectory vs Temp)
            var targetDirectory = DetermineTargetDirectory(baseTargetDirectory, version);
            var driverDirectory = Path.Combine(targetDirectory, "runtimes", osPrefix.ToLowerInvariant(), useCuda ? "cuda" : "cpu");
            var markerFilePath = Path.Combine(driverDirectory, MarkerFileName);

            // 4. Cache Invalidation (O(1) check)
            if (File.Exists(markerFilePath) && File.ReadAllText(markerFilePath).Trim() == version)
            {
                return driverDirectory;
            }

            // 5. Extraction Pipeline (Assembly passed implicitly via OS resolution)
            ExtractResourceToDirectory(osPrefix, driverDirectory);

            // 6. Finalize marker
            File.WriteAllText(markerFilePath, version);

            return driverDirectory;
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    private static string DetermineTargetDirectory(string preferredPath, string version)
    {
        try
        {
            // Test write permissions
            var testFilePath = Path.Combine(preferredPath, $".write_test_{Guid.NewGuid()}");
            File.WriteAllText(testFilePath, string.Empty);
            File.Delete(testFilePath);
            return preferredPath;
        }
        catch (UnauthorizedAccessException)
        {
            return Path.Combine(Path.GetTempPath(), DefaultTempFolderName, version);
        }
    }

    private static void ExtractResourceToDirectory(string osPrefix, string targetDirectory)
    {
        var runtimeAssemblyName = $"InstantAIGate.Runtimes.{osPrefix}";
        Assembly targetAssembly;

        try
        {
            targetAssembly = Assembly.Load(runtimeAssemblyName);
        }
        catch (Exception ex)
        {
            throw new FileNotFoundException(
                $"Failed to load the runtime package '{runtimeAssemblyName}'. " +
                $"Ensure that the NuGet package is installed and referenced in your host project.", ex);
        }

        var resourceName = targetAssembly.GetManifestResourceNames()
            .FirstOrDefault(r => r.EndsWith($"{osPrefix.ToLowerInvariant()}-x64.7z", StringComparison.OrdinalIgnoreCase));

        if (resourceName == null)
        {
            throw new FileNotFoundException($"Embedded runtime archive for {osPrefix} was not found inside '{runtimeAssemblyName}'.");
        }

        using var stream = targetAssembly.GetManifestResourceStream(resourceName);

        if (stream == null)
        {
            throw new InvalidOperationException($"Failed to load the embedded resource stream for '{resourceName}'.");
        }

 
        Directory.CreateDirectory(targetDirectory);

        using var archive = SevenZipArchive.OpenArchive(stream);

        foreach (var entry in archive.Entries.Where(e => !e.IsDirectory))
        {
            var destPath = Path.Combine(targetDirectory, entry.Key!);
            var destDir = Path.GetDirectoryName(destPath);

            if (!string.IsNullOrEmpty(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            using var entryStream = entry.OpenEntryStream();
            using var destStream = File.Create(destPath);
            entryStream.CopyTo(destStream);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var libsToLink = new[] { "libggml", "libggml-base", "libggml-cuda", "libggml-cpu", "libggml-vulkan", "libllama" };

            foreach (var lib in libsToLink)
            {
                var sourceFile = Path.Combine(targetDirectory, $"{lib}.so");
                var linkFile = Path.Combine(targetDirectory, $"{lib}.so.0");

                if (File.Exists(sourceFile))
                {
                  
                    File.Copy(sourceFile, linkFile, true);
                }
            }
        }
    }
}