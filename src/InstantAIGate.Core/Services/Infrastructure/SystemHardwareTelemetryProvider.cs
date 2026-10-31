namespace InstantAIGate.Core.Services.Infrastructure;

using InstantAIGate.Core.Dtos.Infrastructure;
using InstantAIGate.Core.Dtos.Status;
using InstantAIGate.Core.Interfaces.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Diagnostics;

/// <summary>
/// Cross-platform aggregator composing process/CPU/managed-heap metrics (pure .NET),
/// system RAM (Linux <c>/proc/meminfo</c> with GC fallback) and a platform VRAM probe.
/// Thread-safe: safe for concurrent 1 Hz polling and ad-hoc dashboard snapshots.
/// </summary>
public sealed class SystemHardwareTelemetryProvider : IHardwareTelemetryProvider, IDisposable
{
    private readonly IVramProbe _vramProbe;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SystemHardwareTelemetryProvider> _logger;
    private readonly Process _currentProcess = Process.GetCurrentProcess();
    private readonly object _cpuLock = new();
    private TimeSpan _previousCpuTime;
    private DateTimeOffset _previousWallClock;
    private bool _hasPreviousSample;
    private double _lastCpuPercent;
    private bool _disposed;

    public SystemHardwareTelemetryProvider(
        IVramProbe vramProbe,
        TimeProvider? timeProvider = null,
        ILogger<SystemHardwareTelemetryProvider>? logger = null)
    {
        _vramProbe = vramProbe ?? throw new ArgumentNullException(nameof(vramProbe));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger<SystemHardwareTelemetryProvider>.Instance;
    }

    public HardwareMetricsDto CaptureMetrics()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        long processRamBytes = 0;
        long processPrivateBytes = 0;
        double cpuUsagePercent;

        lock (_cpuLock)
        {
            try
            {
                _currentProcess.Refresh();
                processRamBytes = _currentProcess.WorkingSet64;
                processPrivateBytes = _currentProcess.PrivateMemorySize64;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Process memory sample failed; reporting zeros for process counters.");
            }

            cpuUsagePercent = ComputeCpuPercentLocked();
        }

        (long systemTotalRam, long systemAvailableRam) = ReadSystemMemory();
        VramProbeResult vram = ProbeVramSafely();

        return new HardwareMetricsDto
        {
            ProcessRamBytes = processRamBytes,
            ProcessPrivateBytes = processPrivateBytes,
            ManagedHeapBytes = GC.GetTotalMemory(false),
            SystemTotalRamBytes = systemTotalRam,
            SystemAvailableRamBytes = systemAvailableRam,
            CpuUsagePercent = cpuUsagePercent,
            VramTotalBytes = vram.TotalBytes,
            VramUsedBytes = vram.UsedBytes,
            VramProcessUsedBytes = vram.ProcessUsedBytes,
            GpuProviderName = vram.ProviderName
        };
    }

    /// <summary>
    /// Computes process CPU load as delta(TotalProcessorTime) / (delta(wall) * coreCount).
    /// The first sample has no baseline and therefore reports 0.0.
    /// </summary>
    private double ComputeCpuPercentLocked()
    {
        TimeSpan cpuTime = _currentProcess.TotalProcessorTime;
        DateTimeOffset now = _timeProvider.GetUtcNow();
        double percent = _lastCpuPercent;

        if (_hasPreviousSample)
        {
            double wallMs = (now - _previousWallClock).TotalMilliseconds;
            double cpuMs = (cpuTime - _previousCpuTime).TotalMilliseconds;
            if (wallMs > 0.0)
            {
                percent = Math.Clamp(cpuMs / (wallMs * Environment.ProcessorCount) * 100.0, 0.0, 100.0);
            }
        }

        _previousCpuTime = cpuTime;
        _previousWallClock = now;
        _hasPreviousSample = true;
        _lastCpuPercent = percent;
        return percent;
    }

    private static (long TotalBytes, long AvailableBytes) ReadSystemMemory()
    {
        if (OperatingSystem.IsLinux() &&
            ProcMemInfoReader.TryReadSystemMemory("/proc/meminfo", out long totalBytes, out long availableBytes))
        {
            return (totalBytes, availableBytes);
        }

        GCMemoryInfo gcInfo = GC.GetGCMemoryInfo();
        long gcTotal = gcInfo.TotalAvailableMemoryBytes;
        long gcAvailable = Math.Max(0, gcTotal - gcInfo.MemoryLoadBytes);
        return (gcTotal, gcAvailable);
    }

    private VramProbeResult ProbeVramSafely()
    {
        try
        {
            return _vramProbe.Probe();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "VRAM probe threw; reporting unified-memory fallback.");
            return VramProbeResult.Unavailable;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _currentProcess.Dispose();
    }
}
