namespace InstantAIGate.Native.Telemetry;

using InstantAIGate.Core.Dtos.Infrastructure;
using InstantAIGate.Core.Interfaces.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Runtime.InteropServices;

/// <summary>
/// Vendor-agnostic VRAM probe built on the Vulkan API.
/// Reports the total size of all DEVICE_LOCAL memory heaps, which is the physical
/// VRAM visible to the ggml-vulkan backend on NVIDIA, AMD, Intel and MoltenVK hosts,
/// on both Windows and Linux.
/// </summary>
/// <remarks>
/// Function pointers are resolved once via <see cref="NativeLibrary"/> exports
/// (no DllImport duplication across platforms). The Vulkan library handle is kept
/// alive for the probe lifetime and released in <see cref="Dispose"/>.
/// Any failure (missing runtime, CPU-only host, container) degrades to
/// <see cref="VramProbeResult.Unavailable"/>; the probe never throws.
/// </remarks>
public sealed class VulkanVramProbe : IVramProbe, IDisposable
{
    private const uint VkStructureTypeApplicationInfo = 0;
    private const uint VkStructureTypeInstanceCreateInfo = 1;
    private const uint VkMemoryHeapDeviceLocalBit = 1;
    private const uint VkApiVersion11 = (1u << 22) | (1u << 12);
    private const int MaxPhysicalDevices = 16;
    private const int MaxMemoryHeaps = 16;
    private const string ProviderName = "Vulkan (ggml-vulkan backend)";

    private static readonly string[] LibraryCandidates = OperatingSystem.IsWindows()
        ? ["vulkan-1.dll", "vulkan-1"]
        : ["libvulkan.so.1", "libvulkan.so"];

    private readonly ILogger<VulkanVramProbe> _logger;
    private readonly object _syncLock = new();
    private IntPtr _libraryHandle;
    private VkCreateInstanceProc? _vkCreateInstance;
    private VkDestroyInstanceProc? _vkDestroyInstance;
    private VkEnumeratePhysicalDevicesProc? _vkEnumeratePhysicalDevices;
    private VkGetPhysicalDeviceMemoryPropertiesProc? _vkGetPhysicalDeviceMemoryProperties;
    private long _cachedTotalBytes;
    private bool _cacheValid;
    private bool _functionsFailed;
    private bool _disposed;

    public VulkanVramProbe(ILogger<VulkanVramProbe>? logger = null)
    {
        _logger = logger ?? NullLogger<VulkanVramProbe>.Instance;
    }

    public VramProbeResult Probe()
    {
        if (_disposed)
        {
            return VramProbeResult.Unavailable;
        }
        lock (_syncLock)
        {
            if (_cacheValid)
            {
                return new VramProbeResult(_cachedTotalBytes, 0, 0, ProviderName, true);
            }
            if (_functionsFailed)
            {
                return VramProbeResult.Unavailable;
            }
            try
            {
                if (!TryResolveFunctions())
                {
                    _functionsFailed = true;
                    return VramProbeResult.Unavailable;
                }
                long totalBytes = EnumerateDeviceLocalHeapBytes();
                if (totalBytes <= 0)
                {
                    _functionsFailed = true;
                    return VramProbeResult.Unavailable;
                }
                _cachedTotalBytes = totalBytes;
                _cacheValid = true;
                return new VramProbeResult(totalBytes, 0, 0, ProviderName, true);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Vulkan VRAM probe failed; reporting unified-memory fallback.");
                _functionsFailed = true;
                return VramProbeResult.Unavailable;
            }
        }
    }

    private bool TryResolveFunctions()
    {
        if (_vkCreateInstance != null &&
            _vkDestroyInstance != null &&
            _vkEnumeratePhysicalDevices != null &&
            _vkGetPhysicalDeviceMemoryProperties != null)
        {
            return true;
        }
        foreach (string libraryName in LibraryCandidates)
        {
            if (!NativeLibrary.TryLoad(libraryName, out IntPtr handle) || handle == IntPtr.Zero)
            {
                continue;
            }
            if (NativeLibrary.TryGetExport(handle, "vkCreateInstance", out IntPtr createPtr) &&
                NativeLibrary.TryGetExport(handle, "vkDestroyInstance", out IntPtr destroyPtr) &&
                NativeLibrary.TryGetExport(handle, "vkEnumeratePhysicalDevices", out IntPtr enumeratePtr) &&
                NativeLibrary.TryGetExport(handle, "vkGetPhysicalDeviceMemoryProperties", out IntPtr memoryPropsPtr))
            {
                _libraryHandle = handle;
                _vkCreateInstance = Marshal.GetDelegateForFunctionPointer<VkCreateInstanceProc>(createPtr);
                _vkDestroyInstance = Marshal.GetDelegateForFunctionPointer<VkDestroyInstanceProc>(destroyPtr);
                _vkEnumeratePhysicalDevices = Marshal.GetDelegateForFunctionPointer<VkEnumeratePhysicalDevicesProc>(enumeratePtr);
                _vkGetPhysicalDeviceMemoryProperties =
                    Marshal.GetDelegateForFunctionPointer<VkGetPhysicalDeviceMemoryPropertiesProc>(memoryPropsPtr);
                return true;
            }
            NativeLibrary.Free(handle);
        }
        _logger.LogDebug("Vulkan runtime library not found; skipping Vulkan VRAM probe.");
        return false;
    }

    private unsafe long EnumerateDeviceLocalHeapBytes()
    {
        var appInfo = new VkApplicationInfo
        {
            SType = VkStructureTypeApplicationInfo,
            PNext = IntPtr.Zero,
            PApplicationName = IntPtr.Zero,
            ApplicationVersion = 0,
            PEngineName = IntPtr.Zero,
            EngineVersion = 0,
            ApiVersion = VkApiVersion11
        };
        var createInfo = new VkInstanceCreateInfo
        {
            SType = VkStructureTypeInstanceCreateInfo,
            PNext = IntPtr.Zero,
            Flags = 0,
            PApplicationInfo = &appInfo,
            EnabledLayerCount = 0,
            PpEnabledLayerNames = null,
            EnabledExtensionCount = 0,
            PpEnabledExtensionNames = null
        };
        IntPtr instance;
        if (_vkCreateInstance!(&createInfo, IntPtr.Zero, &instance) != 0 || instance == IntPtr.Zero)
        {
            return 0;
        }
        try
        {
            uint deviceCount = 0;
            if (_vkEnumeratePhysicalDevices!(instance, &deviceCount, null) != 0 || deviceCount == 0)
            {
                return 0;
            }
            uint readCount = Math.Min(deviceCount, MaxPhysicalDevices);
            IntPtr* devices = stackalloc IntPtr[(int)readCount];
            if (_vkEnumeratePhysicalDevices!(instance, &readCount, devices) != 0 || readCount == 0)
            {
                return 0;
            }
            long totalDeviceLocalBytes = 0;
            for (uint deviceIndex = 0; deviceIndex < readCount; deviceIndex++)
            {
                VkPhysicalDeviceMemoryProperties props = default;
                _vkGetPhysicalDeviceMemoryProperties!(devices[deviceIndex], &props);
                uint heapCount = Math.Min(props.MemoryHeapCount, MaxMemoryHeaps);
                for (uint heapIndex = 0; heapIndex < heapCount; heapIndex++)
                {
                    ulong heapSize = (ulong)props.MemoryHeaps[heapIndex * 2];
                    uint heapFlags = (uint)props.MemoryHeaps[heapIndex * 2 + 1];
                    if ((heapFlags & VkMemoryHeapDeviceLocalBit) != 0)
                    {
                        totalDeviceLocalBytes += (long)heapSize;
                    }
                }
            }
            return totalDeviceLocalBytes;
        }
        finally
        {
            _vkDestroyInstance!(instance, IntPtr.Zero);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        lock (_syncLock)
        {
            _vkCreateInstance = null;
            _vkDestroyInstance = null;
            _vkEnumeratePhysicalDevices = null;
            _vkGetPhysicalDeviceMemoryProperties = null;
            if (_libraryHandle != IntPtr.Zero)
            {
                try
                {
                    NativeLibrary.Free(_libraryHandle);
                }
                catch
                {
                }
                _libraryHandle = IntPtr.Zero;
            }
        }
    }

    #region Vulkan interop

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private unsafe delegate int VkCreateInstanceProc(VkInstanceCreateInfo* pCreateInfo, IntPtr pAllocator, IntPtr* pInstance);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void VkDestroyInstanceProc(IntPtr instance, IntPtr pAllocator);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private unsafe delegate int VkEnumeratePhysicalDevicesProc(IntPtr instance, uint* pPhysicalDeviceCount, IntPtr* pPhysicalDevices);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private unsafe delegate void VkGetPhysicalDeviceMemoryPropertiesProc(IntPtr physicalDevice, VkPhysicalDeviceMemoryProperties* pMemoryProperties);

    /// <summary>VkApplicationInfo: versions are packed uint32 (VK_MAKE_VERSION), not nested structs.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct VkApplicationInfo
    {
        public uint SType;
        public IntPtr PNext;
        public IntPtr PApplicationName;
        public uint ApplicationVersion;
        public IntPtr PEngineName;
        public uint EngineVersion;
        public uint ApiVersion;
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct VkInstanceCreateInfo
    {
        public uint SType;
        public IntPtr PNext;
        public uint Flags;
        public VkApplicationInfo* PApplicationInfo;
        public uint EnabledLayerCount;
        public IntPtr* PpEnabledLayerNames;
        public uint EnabledExtensionCount;
        public IntPtr* PpEnabledExtensionNames;
    }

    /// <summary>
    /// VkPhysicalDeviceMemoryProperties with primitive-only fixed buffers:
    /// MemoryTypes = 64 entries x 8 bytes as ulong[64];
    /// MemoryHeaps = 16 entries x 16 bytes as ulong[32] (size, flags pairs).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct VkPhysicalDeviceMemoryProperties
    {
        public uint MemoryTypeCount;
        public fixed ulong MemoryTypes[64];
        public uint MemoryHeapCount;
        public fixed ulong MemoryHeaps[MaxMemoryHeaps * 2];
    }

    #endregion
}