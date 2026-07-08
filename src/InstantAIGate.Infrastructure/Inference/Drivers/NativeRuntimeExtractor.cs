using Microsoft.Extensions.Logging;
using SharpCompress.Archives.SevenZip;

namespace InstantAIGate.Infrastructure.Inference.Drivers
{
    /// <summary>
    /// Extracts compressed native runtime archives on first startup.
    /// Each backend folder contains a single 7z archive (e.g., cuda.7z) that
    /// is automatically extracted in-place, allowing large CUDA binaries (~540 MB)
    /// to fit within the NuGet.org 250 MB size limit after LZMA2 compression (~89 MB).
    /// Extraction is skipped when a sentinel file (.extracted) is already present.
    /// </summary>
    [Obsolete("Use LlamaDriverLoader and Driver-prefixed components. This class will be removed in a future release.", error: false)]
    public class NativeRuntimeExtractor
    {
        private const string SentinelFileName = ".extracted";
        private static readonly object _statusLock = new();
        private static bool _isExtracting;

        private readonly ILogger<NativeRuntimeExtractor> _logger;

        /// <summary>
        /// Gets a value indicating whether an extraction operation is currently in progress.
        /// </summary>
        public static bool IsExtracting
        {
            get
            {
                lock (_statusLock)
                {
                    return _isExtracting;
                }
            }
            private set
            {
                lock (_statusLock)
                {
                    _isExtracting = value;
                }
            }
        }

        public NativeRuntimeExtractor(ILogger<NativeRuntimeExtractor> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Scans all backend subdirectories under the given runtime path and
        /// extracts any 7z archives that have not yet been extracted.
        /// </summary>
        /// <param name="runtimesRidPath">
        /// Absolute path to the runtime folder for the current RID,
        /// e.g., bin/Debug/net10.0/runtimes/win-x64
        /// </param>
        public void EnsureExtracted(string runtimesRidPath)
        {
            if (!Directory.Exists(runtimesRidPath))
                return;

            foreach (var backendDir in Directory.GetDirectories(runtimesRidPath))
            {
                EnsureBackendExtracted(backendDir);
            }
        }

        private void EnsureBackendExtracted(string backendDir)
        {
            var backendName = Path.GetFileName(backendDir);
            var sentinelPath = Path.Combine(backendDir, SentinelFileName);

            if (File.Exists(sentinelPath))
            {
                _logger.LogDebug("Backend '{Backend}' already extracted, skipping", backendName);
                return;
            }

            var archiveFiles = Directory.GetFiles(backendDir, "*.7z");
            if (archiveFiles.Length == 0)
                return;

            if (archiveFiles.Length > 1)
            {
                _logger.LogWarning(
                    "Backend '{Backend}' contains multiple 7z archives ({Files}); only the first will be extracted",
                    backendName, string.Join(", ", archiveFiles.Select(Path.GetFileName)));
            }

            var archivePath = archiveFiles[0];
            var archiveName = Path.GetFileName(archivePath);
            var archiveSizeMb = new FileInfo(archivePath).Length / 1_048_576.0;

            _logger.LogInformation(
                "Extracting backend '{Backend}' from {Archive} ({Size:F1} MB)...",
                backendName, archiveName, archiveSizeMb);

            try
            {
                IsExtracting = true;

                using var archive = SevenZipArchive.OpenArchive(archivePath);
                var entries = archive.Entries.Where(e => !e.IsDirectory).ToList();
                var total = entries.Count;
                var current = 0;

                foreach (var entry in entries)
                {
                    var destPath = Path.Combine(backendDir, entry.Key!);
                    var destDir = Path.GetDirectoryName(destPath);

                    if (!string.IsNullOrEmpty(destDir))
                        Directory.CreateDirectory(destDir);

                    using var entryStream = entry.OpenEntryStream();
                    using var destStream = File.Create(destPath);
                    entryStream.CopyTo(destStream);
                    current++;

                    if (current % 10 == 0 || current == total)
                    {
                        _logger.LogDebug(
                            "  Extracted {Current}/{Total}: {Entry}",
                            current, total, entry.Key);
                    }
                }

                WriteSentinel(sentinelPath, archiveName, total);

                _logger.LogInformation(
                    "Backend '{Backend}' extracted successfully ({Total} files)",
                    backendName, total);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to extract backend '{Backend}' from {Archive}", backendName, archivePath);
                throw;
            }
            finally
            {
                IsExtracting = false;
            }
        }

        private static void WriteSentinel(string sentinelPath, string archiveName, int fileCount)
        {
            File.WriteAllText(sentinelPath,
                $"extracted_from={archiveName}{Environment.NewLine}" +
                $"file_count={fileCount}{Environment.NewLine}" +
                $"extracted_at={DateTimeOffset.UtcNow:O}{Environment.NewLine}");
        }
    }
}