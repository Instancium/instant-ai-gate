namespace InstantAIGate.Core.Services.Infrastructure;

using InstantAIGate.Core.Dtos.Infrastructure;
using InstantAIGate.Core.Interfaces.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.IO;

/// <summary>
/// Vendor-agnostic Linux VRAM probe based on DRM sysfs metadata
/// (<c>/sys/class/drm/card*/device/mem_info_vram_total|used</c>) and per-process
/// VRAM attribution from <c>/proc/self/fdinfo/*</c> (<c>drm-memory-vram</c>).
/// </summary>
/// <remarks>
/// Works for AMD/Intel and any driver exposing the sysfs attributes. When the
/// attributes are absent (NVIDIA proprietary driver, CPU-only host, container
/// without GPU passthrough) the probe safely reports
/// <see cref="VramProbeResult.Unavailable"/> and never throws.
/// The sysfs root is injectable for unit testing with a synthetic tree.
/// </remarks>
public sealed class LinuxVramProbe : IVramProbe
{
    private const string DefaultDrmRoot = "/sys/class/drm";
    private const string SelfFdDirectory = "/proc/self/fd";
    private const string SelfFdInfoDirectory = "/proc/self/fdinfo";
    private const string DrmVramPrefix = "drm-memory-vram:";
    private static readonly TimeSpan ProcessScanCacheDuration = TimeSpan.FromSeconds(5);

    private readonly ILogger<LinuxVramProbe> _logger;
    private readonly string _drmRootPath;
    private readonly object _syncLock = new();
    private long _cachedProcessVramBytes;
    private DateTimeOffset _lastProcessScan = DateTimeOffset.MinValue;

    public LinuxVramProbe(ILogger<LinuxVramProbe>? logger = null, string? drmRootPath = null)
    {
        _logger = logger ?? NullLogger<LinuxVramProbe>.Instance;
        _drmRootPath = drmRootPath ?? DefaultDrmRoot;
    }

    public VramProbeResult Probe()
    {
        try
        {
            if (!Directory.Exists(_drmRootPath))
            {
                return VramProbeResult.Unavailable;
            }
            long totalBytes = 0;
            long usedBytes = 0;
            string? driverName = null;
            foreach (string cardDirectory in Directory.EnumerateDirectories(_drmRootPath))
            {
                if (!IsCardDirectoryName(Path.GetFileName(cardDirectory)))
                {
                    continue;
                }
                string deviceDirectory = Path.Combine(cardDirectory, "device");
                if (!TryReadInt64(Path.Combine(deviceDirectory, "mem_info_vram_total"), out long cardTotal) || cardTotal <= 0)
                {
                    continue;
                }
                TryReadInt64(Path.Combine(deviceDirectory, "mem_info_vram_used"), out long cardUsed);
                totalBytes += cardTotal;
                usedBytes += Math.Max(0, cardUsed);
                driverName ??= ResolveDriverName(deviceDirectory);
            }
            if (totalBytes <= 0)
            {
                return VramProbeResult.Unavailable;
            }
            long processUsedBytes = ReadProcessDrmVramBytesCached();
            string providerName = driverName != null ? $"Linux DRM ({driverName})" : "Linux DRM (sysfs)";
            return new VramProbeResult(totalBytes, usedBytes, processUsedBytes, providerName, true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "sysfs VRAM probe failed; reporting unified-memory fallback.");
            return VramProbeResult.Unavailable;
        }
    }

    private long ReadProcessDrmVramBytesCached()
    {
        lock (_syncLock)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            if (now - _lastProcessScan < ProcessScanCacheDuration)
            {
                return _cachedProcessVramBytes;
            }
            _cachedProcessVramBytes = ReadProcessDrmVramBytes();
            _lastProcessScan = now;
            return _cachedProcessVramBytes;
        }
    }

    private static long ReadProcessDrmVramBytes()
    {
        long sumBytes = 0;
        try
        {
            foreach (string fdPath in Directory.EnumerateFiles(SelfFdDirectory))
            {
                string fdInfoPath = Path.Combine(SelfFdInfoDirectory, Path.GetFileName(fdPath));
                try
                {
                    foreach (string line in File.ReadLines(fdInfoPath))
                    {
                        if (line.StartsWith(DrmVramPrefix, StringComparison.Ordinal))
                        {
                            sumBytes += ParseKilobytes(line) * 1024;
                            break;
                        }
                    }
                }
                catch
                {
                    continue;
                }
            }
        }
        catch
        {
            return 0;
        }
        return sumBytes;
    }

    private static long ParseKilobytes(string line)
    {
        string[] parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 && long.TryParse(parts[1], out long value) ? value : 0;
    }

    private static bool IsCardDirectoryName(string name)
    {
        if (!name.StartsWith("card", StringComparison.Ordinal) || name.Length <= 4)
        {
            return false;
        }
        foreach (char c in name.AsSpan(4))
        {
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }
        }
        return true;
    }

    private static bool TryReadInt64(string path, out long value)
    {
        value = 0;
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }
            return long.TryParse(File.ReadAllText(path).AsSpan().Trim(), out value);
        }
        catch
        {
            return false;
        }
    }

    private static string? ResolveDriverName(string deviceDirectory)
    {
        try
        {
            FileSystemInfo? target = File.ResolveLinkTarget(Path.Combine(deviceDirectory, "driver"), returnFinalTarget: false);
            return target?.Name;
        }
        catch
        {
            return null;
        }
    }
}
