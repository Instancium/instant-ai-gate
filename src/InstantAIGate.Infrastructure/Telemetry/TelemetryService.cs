using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using InstantAIGate.Application.Dtos.Telemetry;
using InstantAIGate.Application.Interfaces;
using InstantAIGate.Application.Interfaces.Inference;
using InstantAIGate.Infrastructure.NvmlNative;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace InstantAIGate.Infrastructure.Telemetry
{
    /// <summary>
    /// Compiles host operational metrics and maps underlying runtime execution layers.
    /// </summary>
    public sealed class TelemetryService : ITelemetryService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly NvmlProvider _nvmlProvider;
        private readonly IDriverStateProvider _driverStateProvider;
        private readonly ILogger<TelemetryService> _logger;

        private DateTime _lastCpuCheck = DateTime.UtcNow;
        private TimeSpan _lastCpuTime = TimeSpan.Zero;
        private int _lastCalculatedCpuUsage = 0;
        private readonly object _cpuLock = new();

        public TelemetryService(
            IServiceScopeFactory scopeFactory,
            NvmlProvider nvmlProvider,
            IDriverStateProvider driverStateProvider,
            ILogger<TelemetryService> logger)
        {
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _nvmlProvider = nvmlProvider ?? throw new ArgumentNullException(nameof(nvmlProvider));
            _driverStateProvider = driverStateProvider ?? throw new ArgumentNullException(nameof(driverStateProvider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _lastCpuTime = Process.GetCurrentProcess().TotalProcessorTime;
        }

        public SystemTelemetry GetCurrentSystemTelemetry()
        {
            var telemetry = new SystemTelemetry
            {
                IsExtractingDrivers = _driverStateProvider.IsExtracting,
                Gpu = new GpuStatus
                {
                    UsedMemoryGb = _nvmlProvider.GetUsedMemoryGb(),
                    TotalMemoryGb = _nvmlProvider.GetTotalMemoryGb(),
                    TemperatureCelsius = _nvmlProvider.GetTemperature(),
                    UtilizationPercent = _nvmlProvider.GetUtilization()
                },
                System = GetSystemHardwareStatus()
            };

            if (telemetry.IsExtractingDrivers)
            {
                return telemetry;
            }

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var modelManager = scope.ServiceProvider.GetRequiredService<IModelManager>();

                var nativeDetails = modelManager.GetNativeDetails();
                var activeModels = modelManager.ActiveModels;
                var semaphores = modelManager.UserSemaphores;

                foreach (var detail in nativeDetails)
                {
                    var repoId = detail.RepoId;
                    activeModels.TryGetValue(repoId, out var config);
                    semaphores.TryGetValue(repoId, out var sem);

                    var maxUsers = config?.MaxContexts ?? 0;
                    var activeUsers = sem != null && maxUsers > 0 ? maxUsers - sem.CurrentCount : 0;

                    telemetry.Models.Add(new ModelTelemetry
                    {
                        RepoId = repoId,
                        IsLoaded = true,
                        ContextSize = detail.ContextSize,
                        GpuLayers = detail.GpuLayers,
                        Threads = detail.Threads,
                        FlashAttention = detail.FlashAttention,
                        IdleContextsCount = detail.IdleContextsCount,
                        MaxParallelUsers = maxUsers,
                        ActiveUsersCount = activeUsers,
                        IsQueueWaiting = sem != null && sem.CurrentCount == 0
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to aggregate native model telemetry.");
            }

            return telemetry;
        }

        private SystemHardwareStatus GetSystemHardwareStatus()
        {
            var status = new SystemHardwareStatus();

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                GetWindowsMemory(out var totalGb, out var usedGb);
                status.TotalRamGb = totalGb;
                status.UsedRamGb = usedGb;
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                GetLinuxMemory(out var totalGb, out var usedGb);
                status.TotalRamGb = totalGb;
                status.UsedRamGb = usedGb;
            }
            else
            {
                status.TotalRamGb = 16.0;
                status.UsedRamGb = Process.GetCurrentProcess().WorkingSet64 / 1024.0 / 1024.0 / 1024.0;
            }

            status.CpuUtilizationPercent = CalculateCpuUsage();
            return status;
        }

        private int CalculateCpuUsage()
        {
            lock (_cpuLock)
            {
                var currentTime = DateTime.UtcNow;
                var currentCpuTime = Process.GetCurrentProcess().TotalProcessorTime;
                var timeWindow = currentTime - _lastCpuCheck;

                if (timeWindow.TotalMilliseconds < 500)
                {
                    return _lastCalculatedCpuUsage;
                }

                var systemTimeDelta = timeWindow.TotalMilliseconds * Environment.ProcessorCount;
                var cpuTimeDelta = (currentCpuTime - _lastCpuTime).TotalMilliseconds;

                _lastCpuCheck = currentTime;
                _lastCpuTime = currentCpuTime;

                if (systemTimeDelta <= 0)
                {
                    return _lastCalculatedCpuUsage;
                }

                var usage = (cpuTimeDelta / systemTimeDelta) * 100;
                _lastCalculatedCpuUsage = Math.Clamp((int)Math.Round(usage), 0, 100);

                return _lastCalculatedCpuUsage;
            }
        }

        #region Memory Helpers

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
            public MEMORYSTATUSEX() => dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>();
        }

        private void GetWindowsMemory(out double totalGb, out double usedGb)
        {
            var memStatus = new MEMORYSTATUSEX();
            if (GlobalMemoryStatusEx(ref memStatus))
            {
                totalGb = memStatus.ullTotalPhys / 1024.0 / 1024.0 / 1024.0;
                usedGb = (memStatus.ullTotalPhys - memStatus.ullAvailPhys) / 1024.0 / 1024.0 / 1024.0;
            }
            else
            {
                totalGb = 0; usedGb = 0;
            }
        }

        private void GetLinuxMemory(out double totalGb, out double usedGb)
        {
            totalGb = 0; usedGb = 0;
            try
            {
                var lines = File.ReadAllLines("/proc/meminfo");
                double memTotalKib = 0;
                double memAvailableKib = 0;

                foreach (var line in lines)
                {
                    if (line.StartsWith("MemTotal:"))
                    {
                        memTotalKib = double.Parse(System.Text.RegularExpressions.Regex.Match(line, @"\d+").Value);
                    }
                    if (line.StartsWith("MemAvailable:"))
                    {
                        memAvailableKib = double.Parse(System.Text.RegularExpressions.Regex.Match(line, @"\d+").Value);
                    }
                }

                totalGb = memTotalKib / 1024.0 / 1024.0;
                usedGb = (memTotalKib - memAvailableKib) / 1024.0 / 1024.0;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse Linux memory limits.");
            }
        }

        #endregion
    }
}