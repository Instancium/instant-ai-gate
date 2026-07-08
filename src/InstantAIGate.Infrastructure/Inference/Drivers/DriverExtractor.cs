using System;
using System.IO;
using System.Linq;
using System.Reflection;
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
        // 1. Local Debug Override
        if (!string.IsNullOrWhiteSpace(localPath))
        {
            return localPath;
        }

        var assembly = typeof(DriverExtractor).Assembly;
        var version = assembly.GetName().Version?.ToString() ?? "1.0.0";
        var baseTargetDirectory = AppContext.BaseDirectory;

        // 2. Cross-process synchronization
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
                return driverDirectory; // Already extracted and up-to-date
            }

            // 5. Extraction Pipeline
            ExtractResourceToDirectory(assembly, osPrefix, driverDirectory);

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

    private static void ExtractResourceToDirectory(Assembly assembly, string osPrefix, string targetDirectory)
    {
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(r => r.EndsWith($"{osPrefix.ToLowerInvariant()}-x64.7z", StringComparison.OrdinalIgnoreCase));

        if (resourceName == null)
        {
            throw new FileNotFoundException($"Embedded runtime archive for {osPrefix} was not found in the assembly.");
        }

        if (Directory.Exists(targetDirectory))
        {
            Directory.Delete(targetDirectory, true);
        }

        Directory.CreateDirectory(targetDirectory);

        using var stream = assembly.GetManifestResourceStream(resourceName);
        using var archive = SevenZipArchive.Open(stream);

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
    }
}