namespace InstantAIGate.Core.Services.Infrastructure;

using InstantAIGate.Core.Dtos.Infrastructure;
using InstantAIGate.Core.Interfaces.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;

/// <summary>
/// Aggregates results from multiple <see cref="IVramProbe"/> strategies and
/// returns the most informative snapshot. Platform-specific probes (DXGI/sysfs)
/// provide per-process and device-wide usage; the Vulkan probe provides an
/// authoritative total capacity that works on Linux+NVIDIA.
/// </summary>
/// <remarks>
/// Selection rules:
/// <list type="bullet">
///   <item>If any probe reports <c>IsGpuPresent == true</c>, the result is GPU-present.</item>
///   <item><c>TotalBytes</c> is the maximum across probes (Vulkan usually wins on Linux+NVIDIA).</item>
///   <item><c>UsedBytes</c> and <c>ProcessUsedBytes</c> are taken from the first probe that reports them non-zero
///       (DXGI/PDH on Windows, sysfs fdinfo on Linux).</item>
///   <item><c>ProviderName</c> is the name of the probe that supplied the total capacity.</item>
/// </list>
/// </remarks>
public sealed class CompositeVramProbe : IVramProbe
{
    private readonly IReadOnlyList<IVramProbe> _probes;
    private readonly ILogger<CompositeVramProbe> _logger;

    public CompositeVramProbe(IEnumerable<IVramProbe> probes, ILogger<CompositeVramProbe>? logger = null)
    {
        _probes = probes is IReadOnlyList<IVramProbe> list ? list : new List<IVramProbe>(probes);
        _logger = logger ?? NullLogger<CompositeVramProbe>.Instance;
    }

    public VramProbeResult Probe()
    {
        VramProbeResult best = VramProbeResult.Unavailable;

        foreach (IVramProbe probe in _probes)
        {
            if (probe is null)
            {
                continue;
            }

            try
            {
                VramProbeResult current = probe.Probe();
                if (!current.IsGpuPresent)
                {
                    continue;
                }

                if (!best.IsGpuPresent || current.TotalBytes > best.TotalBytes)
                {
                    best = current;
                }
                else if (current.TotalBytes == best.TotalBytes && current.UsedBytes > best.UsedBytes)
                {
                    best = current;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "VRAM probe {ProbeType} threw; skipping.", probe.GetType().Name);
            }
        }

        return best;
    }
}
