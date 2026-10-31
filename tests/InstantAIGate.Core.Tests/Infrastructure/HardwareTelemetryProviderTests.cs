namespace InstantAIGate.Core.Tests.Infrastructure;

using InstantAIGate.Core.Dtos.Infrastructure;
using InstantAIGate.Core.Dtos.Status;
using InstantAIGate.Core.Interfaces.Infrastructure;
using InstantAIGate.Core.Services.Infrastructure;
using InstantAIGate.Native.Telemetry;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Xunit;

public sealed class HardwareTelemetryProviderTests : IDisposable
{
    private readonly List<string> _tempDirectories = new();
    private readonly List<IDisposable> _disposables = new();

    private SystemHardwareTelemetryProvider CreateProvider(IVramProbe vramProbe)
    {
        var provider = new SystemHardwareTelemetryProvider(
            vramProbe,
            TimeProvider.System,
            NullLogger<SystemHardwareTelemetryProvider>.Instance);
        _disposables.Add(provider);
        return provider;
    }

    private string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), $"InstantAIGate_HwTel_{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        _tempDirectories.Add(path);
        return path;
    }

    [Fact]
    public void CaptureMetrics_WarmCall_CompletesWithinBudgetAndReportsCoreMetrics()
    {
        var provider = CreateProvider(new LinuxVramProbe());
        provider.CaptureMetrics();
        provider.CaptureMetrics();

        var stopwatch = Stopwatch.StartNew();
        HardwareMetricsDto dto = provider.CaptureMetrics();
        stopwatch.Stop();

        Assert.True(
            stopwatch.Elapsed.TotalMilliseconds < 15,
            $"CaptureMetrics took {stopwatch.Elapsed.TotalMilliseconds:F1}ms (budget: 15ms).");
        Assert.True(dto.ProcessRamBytes > 0, "ProcessRamBytes must be non-zero.");
        Assert.True(dto.ManagedHeapBytes > 0, "ManagedHeapBytes must be non-zero.");
        Assert.True(dto.SystemTotalRamBytes > 0, "SystemTotalRamBytes must be non-zero.");
        Assert.True(dto.SystemAvailableRamBytes >= 0);
        Assert.InRange(dto.CpuUsagePercent, 0.0, 100.0);
        Assert.False(string.IsNullOrWhiteSpace(dto.GpuProviderName));
    }

    [Fact]
    public void CaptureMetrics_ParallelCalls_RemainThreadSafeAndConsistent()
    {
        var provider = CreateProvider(new LinuxVramProbe());
        provider.CaptureMetrics();

        var results = new ConcurrentBag<HardwareMetricsDto>();
        var exceptions = new ConcurrentBag<Exception>();

        Parallel.For(0, 64, _ =>
        {
            try
            {
                results.Add(provider.CaptureMetrics());
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        });

        Assert.Empty(exceptions);
        Assert.Equal(64, results.Count);
        Assert.All(results, dto =>
        {
            Assert.True(dto.ProcessRamBytes > 0);
            Assert.True(dto.SystemTotalRamBytes > 0);
            Assert.InRange(dto.CpuUsagePercent, 0.0, 100.0);
        });
    }

    [Fact]
    public void CaptureMetrics_PropagatesVramProbeValues()
    {
        var probe = new Mock<IVramProbe>();
        probe.Setup(p => p.Probe()).Returns(new VramProbeResult(
            TotalBytes: 8L * 1024 * 1024 * 1024,
            UsedBytes: 3L * 1024 * 1024 * 1024,
            ProcessUsedBytes: 2L * 1024 * 1024 * 1024,
            ProviderName: "Test GPU Adapter",
            IsGpuPresent: true));

        var provider = CreateProvider(probe.Object);
        HardwareMetricsDto dto = provider.CaptureMetrics();

        Assert.Equal(8L * 1024 * 1024 * 1024, dto.VramTotalBytes);
        Assert.Equal(3L * 1024 * 1024 * 1024, dto.VramUsedBytes);
        Assert.Equal(2L * 1024 * 1024 * 1024, dto.VramProcessUsedBytes);
        Assert.Equal("Test GPU Adapter", dto.GpuProviderName);
    }

    [Fact]
    public void CaptureMetrics_WhenProbeThrows_FallsBackToUnifiedMemory()
    {
        var probe = new Mock<IVramProbe>();
        probe.Setup(p => p.Probe()).Throws(new InvalidOperationException("probe failure"));

        var provider = CreateProvider(probe.Object);
        HardwareMetricsDto dto = provider.CaptureMetrics();

        Assert.Equal(0, dto.VramTotalBytes);
        Assert.Equal(0, dto.VramUsedBytes);
        Assert.Equal(0, dto.VramProcessUsedBytes);
        Assert.Equal(VramProbeResult.FallbackProviderName, dto.GpuProviderName);
        Assert.True(dto.ProcessRamBytes > 0, "Core metrics must survive a failing VRAM probe.");
    }

    [Fact]
    public void LinuxVramProbe_WithSyntheticSysfsTree_ParsesTotalsAndIgnoresConnectors()
    {
        string root = CreateTempDirectory();
        string cardDevice = Path.Combine(root, "card0", "device");
        Directory.CreateDirectory(cardDevice);
        File.WriteAllText(Path.Combine(cardDevice, "mem_info_vram_total"), "8589934592\n");
        File.WriteAllText(Path.Combine(cardDevice, "mem_info_vram_used"), "2147483648\n");

        string connectorDevice = Path.Combine(root, "card0-HDMI-A-1", "device");
        Directory.CreateDirectory(connectorDevice);
        File.WriteAllText(Path.Combine(connectorDevice, "mem_info_vram_total"), "111111\n");
        File.WriteAllText(Path.Combine(connectorDevice, "mem_info_vram_used"), "222222\n");

        var probe = new LinuxVramProbe(NullLogger<LinuxVramProbe>.Instance, root);
        VramProbeResult result = probe.Probe();

        Assert.True(result.IsGpuPresent);
        Assert.Equal(8589934592, result.TotalBytes);
        Assert.Equal(2147483648, result.UsedBytes);
        Assert.Equal("Linux DRM (sysfs)", result.ProviderName);
    }

    [Fact]
    public void LinuxVramProbe_WithoutSysfsEntries_ReturnsUnavailableFallback()
    {
        string root = CreateTempDirectory();
        var probe = new LinuxVramProbe(NullLogger<LinuxVramProbe>.Instance, Path.Combine(root, "missing"));
        VramProbeResult result = probe.Probe();

        Assert.False(result.IsGpuPresent);
        Assert.Equal(0, result.TotalBytes);
        Assert.Equal(0, result.UsedBytes);
        Assert.Equal(VramProbeResult.FallbackProviderName, result.ProviderName);
    }

    [Fact]
    public void WindowsVramProbe_OnNonWindowsHost_ReturnsFallbackWithoutThrowing()
    {
        using var probe = new WindowsVramProbe(NullLogger<WindowsVramProbe>.Instance);
        VramProbeResult result = probe.Probe();

        if (!OperatingSystem.IsWindows())
        {
            Assert.False(result.IsGpuPresent);
            Assert.Equal(0, result.TotalBytes);
            Assert.Equal(VramProbeResult.FallbackProviderName, result.ProviderName);
        }
        else
        {
            Assert.False(string.IsNullOrWhiteSpace(result.ProviderName));
        }
    }

    public void Dispose()
    {
        foreach (IDisposable disposable in _disposables)
        {
            disposable.Dispose();
        }

        _disposables.Clear();

        foreach (string directory in _tempDirectories)
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch
            {
            }
        }

        _tempDirectories.Clear();
    }
}
