namespace InstantAIGate.Core.Interfaces.Infrastructure;

using InstantAIGate.Core.Dtos.Infrastructure;

/// <summary>
/// Platform strategy for reading video-memory budget and usage outside of the
/// managed heap (DXGI on Windows, DRM sysfs on Linux, graceful zeros elsewhere).
/// </summary>
public interface IVramProbe
{
    VramProbeResult Probe();
}
