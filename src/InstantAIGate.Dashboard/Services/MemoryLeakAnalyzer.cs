namespace InstantAIGate.Dashboard.Services;

using InstantAIGate.Core.Dtos.Status;
using System;
using System.Collections.Generic;
using System.Linq;

public sealed record MemoryLeakReport
{
    public long TotalModelWeightsBytes { get; init; }
    public long EstimatedKvCacheBytes { get; init; }
    public long EstimatedGatewayVramBytes => TotalModelWeightsBytes + EstimatedKvCacheBytes;
    public long UnaccountedVramDeltaBytes { get; init; }
    public long ProcessWorkingSetBytes { get; init; }
    public long ProcessPrivateBytes { get; init; }
    public long ManagedHeapBytes { get; init; }
    public long PrivateUnmanagedBytes { get; init; }
    public int ActiveLeases { get; init; }
    public int IdlePooledContexts { get; init; }
    public int TotalAllocatedContexts => ActiveLeases + IdlePooledContexts;
    public int ActiveSessionsCount { get; init; }
    public double VramUtilizationPercent { get; init; }
    public bool IsVramSpilloverRisk => VramUtilizationPercent >= 95.0;
    public bool IsNativeRetentionSuspected { get; init; }
    public string? RetentionDiagnosticMessage { get; init; }
}

public static class MemoryLeakAnalyzer
{
    private const long RetentionDeltaThresholdBytes = 256L * 1024 * 1024;

    public static MemoryLeakReport Analyze(
        DashboardTelemetrySnapshot? current,
        IReadOnlyList<DashboardTelemetrySnapshot>? history = null)
    {
        if (current is null)
        {
            return new MemoryLeakReport();
        }

        var hw = current.Hardware;
        var models = current.Models ?? [];
        var sessions = current.Sessions ?? [];

        long totalWeightsBytes = 0;
        long estimatedKvBytes = 0;
        int activeLeases = 0;
        int idleContexts = 0;

        foreach (var model in models)
        {
            totalWeightsBytes += Math.Max(0, model.ModelWeightsBytes);
            activeLeases += Math.Max(0, model.ActiveLeases);
            idleContexts += Math.Max(0, model.IdlePooledContexts);

            int allocatedSlots = Math.Max(0, model.ActiveLeases + model.IdlePooledContexts);
            estimatedKvBytes += allocatedSlots * EstimateSingleContextKvBytes(model);
        }

        long expectedGatewayVram = totalWeightsBytes + estimatedKvBytes;
        long referenceVramUsed = hw.VramProcessUsedBytes > 0 ? hw.VramProcessUsedBytes : hw.VramUsedBytes;
        long unaccountedVramDelta = Math.Max(0, referenceVramUsed - expectedGatewayVram);

        long effectivePrivateBytes = hw.ProcessPrivateBytes > 0
            ? hw.ProcessPrivateBytes
            : hw.ProcessRamBytes;
        long privateUnmanagedBytes = Math.Max(0, effectivePrivateBytes - hw.ManagedHeapBytes);

        double vramUtilPercent = hw.VramTotalBytes > 0
            ? Math.Clamp((double)hw.VramUsedBytes / hw.VramTotalBytes * 100.0, 0.0, 100.0)
            : 0.0;

        bool retentionSuspected = false;
        string? diagnosticMessage = null;

        int totalAllocatedContexts = activeLeases + idleContexts;
        bool isIdleState = sessions.Count == 0 && totalAllocatedContexts == 0;

        if (isIdleState)
        {
            if (hw.VramProcessUsedBytes > 0 &&
                hw.VramProcessUsedBytes - totalWeightsBytes > RetentionDeltaThresholdBytes)
            {
                retentionSuspected = true;
                long excessMb = (hw.VramProcessUsedBytes - totalWeightsBytes) / (1024 * 1024);
                diagnosticMessage =
                    $"Suspected Native Memory Retention: Process VRAM exceeds loaded weights by {excessMb} MB with 0 allocated contexts.";
            }
            else if (history is { Count: >= 3 })
            {
                bool hadAllocatedContextsBefore = false;
                long minIdleUnmanagedBytes = long.MaxValue;

                foreach (var sample in history)
                {
                    int sampleContexts = sample.Models?.Sum(m => m.ActiveLeases + m.IdlePooledContexts) ?? 0;
                    long sampleWeights = sample.Models?.Sum(m => m.ModelWeightsBytes) ?? 0;
                    long samplePrivate = sample.Hardware.ProcessPrivateBytes > 0
                        ? sample.Hardware.ProcessPrivateBytes
                        : sample.Hardware.ProcessRamBytes;
                    long sampleUnmanagedNet = Math.Max(0, samplePrivate - sample.Hardware.ManagedHeapBytes - sampleWeights);

                    if (sampleContexts > 0 || (sample.Sessions?.Count ?? 0) > 0)
                    {
                        hadAllocatedContextsBefore = true;
                    }
                    else if (sampleUnmanagedNet < minIdleUnmanagedBytes)
                    {
                        minIdleUnmanagedBytes = sampleUnmanagedNet;
                    }
                }

                long currentUnmanagedNet = Math.Max(0, privateUnmanagedBytes - totalWeightsBytes);
                if (hadAllocatedContextsBefore &&
                    minIdleUnmanagedBytes < long.MaxValue &&
                    currentUnmanagedNet - minIdleUnmanagedBytes > RetentionDeltaThresholdBytes)
                {
                    retentionSuspected = true;
                    long driftMb = (currentUnmanagedNet - minIdleUnmanagedBytes) / (1024 * 1024);
                    diagnosticMessage =
                        $"Suspected Native Memory Retention: Unmanaged heap retained +{driftMb} MB above idle baseline after context release.";
                }
            }
        }

        return new MemoryLeakReport
        {
            TotalModelWeightsBytes = totalWeightsBytes,
            EstimatedKvCacheBytes = estimatedKvBytes,
            UnaccountedVramDeltaBytes = unaccountedVramDelta,
            ProcessWorkingSetBytes = hw.ProcessRamBytes,
            ProcessPrivateBytes = effectivePrivateBytes,
            ManagedHeapBytes = hw.ManagedHeapBytes,
            PrivateUnmanagedBytes = privateUnmanagedBytes,
            ActiveLeases = activeLeases,
            IdlePooledContexts = idleContexts,
            ActiveSessionsCount = sessions.Count,
            VramUtilizationPercent = vramUtilPercent,
            IsNativeRetentionSuspected = retentionSuspected,
            RetentionDiagnosticMessage = diagnosticMessage
        };
    }

    public static long EstimateSingleContextKvBytes(ModelPoolTelemetryDto model)
    {
        int layers = model.TotalLayers > 0 ? model.TotalLayers : Math.Max(1, model.GpuLayers);
        int ctxSize = model.ContextSize > 0 ? model.ContextSize : 2048;

        double bytesPerElement = (model.KvCacheQuantization ?? "F16").ToUpperInvariant() switch
        {
            "F32" => 4.0,
            "Q8_0" or "Q8_K" => 1.0625,
            "Q5_K" or "Q5_0" or "Q5_1" => 0.6875,
            "Q4_0" or "Q4_K" => 0.5625,
            _ => 2.0
        };

        const int baselineKvDim = 1024;
        return (long)(ctxSize * (long)layers * 2L * baselineKvDim * bytesPerElement);
    }
}
