namespace InstantAIGate.Native.Telemetry;

using InstantAIGate.Core.Dtos.Infrastructure;
using InstantAIGate.Core.Interfaces.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

/// <summary>
/// Vendor-agnostic Windows VRAM probe built on DXGI COM interop:
/// CreateDXGIFactory1 -> EnumAdapters1 -> IDXGIAdapter1::GetDesc1 (capacity, name,
/// software-adapter filter) and IDXGIAdapter3::QueryVideoMemoryInfo (budget and
/// process-attributed usage). Device-wide usage is refined via PDH when available.
/// </summary>
/// <remarks>
/// Works for NVIDIA, AMD and Intel adapters without proprietary SDKs. Software
/// adapters (WARP / Basic Render Driver) are skipped. Any failure degrades to
/// <see cref="VramProbeResult.Unavailable"/>; the probe never throws.
/// </remarks>
public sealed class WindowsVramProbe : IVramProbe, IDisposable
{
    private const uint DxgiAdapterFlagSoftware = 2;
    private const int MaxAdapterCount = 64;
    private static readonly TimeSpan ReinitThrottle = TimeSpan.FromSeconds(30);

    private readonly ILogger<WindowsVramProbe> _logger;
    private readonly PdhGpuMemoryReader _pdhReader = new();
    private readonly object _syncLock = new();
    private List<AdapterEntry>? _adapters;
    private bool _initFailed;
    private DateTimeOffset _lastInitAttempt = DateTimeOffset.MinValue;
    private bool _disposed;

    public WindowsVramProbe(ILogger<WindowsVramProbe>? logger = null)
    {
        _logger = logger ?? NullLogger<WindowsVramProbe>.Instance;
    }

    public VramProbeResult Probe()
    {
        if (_disposed || !OperatingSystem.IsWindows())
        {
            return VramProbeResult.Unavailable;
        }

        lock (_syncLock)
        {
            try
            {
                EnsureAdaptersInitialized();
                if (_adapters is null || _adapters.Count == 0)
                {
                    return VramProbeResult.Unavailable;
                }

                long dedicatedTotal = 0;
                long budget = 0;
                long currentUsage = 0;
                string providerName = "DXGI (Windows)";
                foreach (AdapterEntry entry in _adapters)
                {
                    dedicatedTotal += (long)(ulong)entry.Description.DedicatedVideoMemory;
                    if (providerName.Length <= 16 && !string.IsNullOrWhiteSpace(entry.Description.Description))
                    {
                        providerName = $"DXGI: {entry.Description.Description.Trim()}";
                    }

                    if (entry.Adapter3 is null)
                    {
                        continue;
                    }

                    if (entry.Adapter3.QueryVideoMemoryInfo(0, out D3DQueryVideoMemoryInfo info) == 0)
                    {
                        budget += (long)info.Budget;
                        currentUsage += (long)info.CurrentUsage;
                    }
                }

                long totalBytes = dedicatedTotal > 0 ? dedicatedTotal : budget;
                if (totalBytes <= 0)
                {
                    return VramProbeResult.Unavailable;
                }

                long deviceUsedBytes = _pdhReader.TryGetDeviceDedicatedUsageBytes(out long pdhUsedBytes)
                    ? pdhUsedBytes
                    : currentUsage;

                return new VramProbeResult(totalBytes, deviceUsedBytes, currentUsage, providerName, true);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "DXGI VRAM probe failed; reporting unified-memory fallback.");
                return VramProbeResult.Unavailable;
            }
        }
    }

    private void EnsureAdaptersInitialized()
    {
        if (_adapters is not null)
        {
            return;
        }

        if (_initFailed && DateTimeOffset.UtcNow - _lastInitAttempt < ReinitThrottle)
        {
            return;
        }

        _lastInitAttempt = DateTimeOffset.UtcNow;
        _adapters = TryEnumerateAdapters();
        _initFailed = _adapters is null || _adapters.Count == 0;
    }

    private List<AdapterEntry>? TryEnumerateAdapters()
    {
        Guid factoryRiid = typeof(IDXGIFactory1).GUID;
        if (CreateDXGIFactory1(ref factoryRiid, out IntPtr factoryPtr) != 0 || factoryPtr == IntPtr.Zero)
        {
            return null;
        }

        var result = new List<AdapterEntry>();
        try
        {
            object factoryRcw = Marshal.GetObjectForIUnknown(factoryPtr);
            try
            {
                if (factoryRcw is not IDXGIFactory1 factory)
                {
                    return null;
                }

                for (uint index = 0; index < MaxAdapterCount; index++)
                {
                    if (factory.EnumAdapters1(index, out IntPtr adapterPtr) != 0 || adapterPtr == IntPtr.Zero)
                    {
                        break;
                    }

                    try
                    {
                        object adapterRcw = Marshal.GetObjectForIUnknown(adapterPtr);
                        if (adapterRcw is IDXGIAdapter1 adapter1 && adapter1.GetDesc1(out DxgiAdapterDesc1 desc) == 0 &&
                            (desc.Flags & DxgiAdapterFlagSoftware) == 0)
                        {
                            result.Add(new AdapterEntry(adapter1, adapterRcw as IDXGIAdapter3, desc));
                        }
                        else
                        {
                            Marshal.ReleaseComObject(adapterRcw);
                        }
                    }
                    finally
                    {
                        Marshal.Release(adapterPtr);
                    }
                }
            }
            finally
            {
                Marshal.ReleaseComObject(factoryRcw);
            }
        }
        finally
        {
            Marshal.Release(factoryPtr);
        }

        return result;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _pdhReader.Dispose();

        lock (_syncLock)
        {
            if (_adapters is null)
            {
                return;
            }

            foreach (AdapterEntry entry in _adapters)
            {
                try
                {
                    Marshal.ReleaseComObject(entry.Adapter);
                }
                catch
                {
                }
            }

            _adapters = null;
        }
    }

    private sealed record AdapterEntry(IDXGIAdapter1 Adapter, IDXGIAdapter3? Adapter3, DxgiAdapterDesc1 Description);

    #region DXGI interop

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DxgiAdapterDesc1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public UIntPtr DedicatedVideoMemory;
        public UIntPtr DedicatedSystemMemory;
        public UIntPtr SharedSystemMemory;
        public long AdapterLuid;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct D3DQueryVideoMemoryInfo
    {
        public ulong Budget;
        public ulong CurrentUsage;
        public ulong AvailableForReservation;
        public ulong CurrentReservation;
    }

    [DllImport("dxgi.dll", ExactSpelling = true)]
    private static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr ppFactory);

    [ComImport, Guid("770aae78-f26f-4dba-a829-253c83d1b387"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IDXGIFactory1
    {
        int SetPrivateData(ref Guid name, int dataSize, IntPtr pData);
        int SetPrivateDataInterface(ref Guid name, IntPtr pUnknown);
        int GetPrivateData(ref Guid name, ref int dataSize, IntPtr pData);
        int GetParent(ref Guid riid, out IntPtr ppParent);
        int GetDevice(ref Guid riid, out IntPtr ppDevice);
        int EnumAdapters(uint adapter, out IntPtr ppAdapter);
        int MakeWindowAssociation(IntPtr windowHandle, uint flags);
        int GetWindowAssociation(out IntPtr pWindowHandle);
        int CreateSwapChain(IntPtr pDevice, IntPtr pDesc, out IntPtr ppSwapChain);
        int CreateSoftwareAdapter(IntPtr module, out IntPtr ppAdapter);
        int EnumAdapters1(uint adapter, out IntPtr ppAdapter);
        bool IsCurrent();
    }

    [ComImport, Guid("29038f61-3839-4626-91fd-086879011a05"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IDXGIAdapter1
    {
        int SetPrivateData(ref Guid name, int dataSize, IntPtr pData);
        int SetPrivateDataInterface(ref Guid name, IntPtr pUnknown);
        int GetPrivateData(ref Guid name, ref int dataSize, IntPtr pData);
        int GetParent(ref Guid riid, out IntPtr ppParent);
        int GetDevice(ref Guid riid, out IntPtr ppDevice);
        int EnumOutputs(uint output, out IntPtr ppOutput);
        int GetDescReserved(IntPtr pDesc);
        int ComparePreferenceReserved(IntPtr pDesc1, IntPtr pDesc2);
        int GetDesc1(out DxgiAdapterDesc1 pDesc);
        int IsRemoteReserved(out int remote);
    }

    [ComImport, Guid("6459a394-9ef0-4413-9469-bad981e37a2b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IDXGIAdapter3
    {
        int SetPrivateData(ref Guid name, int dataSize, IntPtr pData);
        int SetPrivateDataInterface(ref Guid name, IntPtr pUnknown);
        int GetPrivateData(ref Guid name, ref int dataSize, IntPtr pData);
        int GetParent(ref Guid riid, out IntPtr ppParent);
        int GetDevice(ref Guid riid, out IntPtr ppDevice);
        int EnumOutputs(uint output, out IntPtr ppOutput);
        int GetDescReserved(IntPtr pDesc);
        int ComparePreferenceReserved(IntPtr pDesc1, IntPtr pDesc2);
        int GetDesc1(out DxgiAdapterDesc1 pDesc);
        int IsRemoteReserved(out int remote);
        int GetDesc2Reserved(IntPtr pDesc);
        int RegisterHardwareContentProtectionTeardownStatusNotificationReserved(IntPtr hEvent, out long cookie);
        int UnregisterHardwareContentProtectionTeardownStatusNotificationReserved(long cookie);
        int QueryVideoMemoryInfo(uint nodeIndex, out D3DQueryVideoMemoryInfo pVideoMemoryInfo);
    }

    #endregion
}
