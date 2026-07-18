using System.Runtime.InteropServices;

namespace InstantAIGate.Infrastructure.NvmlNative
{
    internal static class NvmlApi
    {
        private static readonly IntPtr _libHandle;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int NvmlInitDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int NvmlDeviceGetHandleByIndexDelegate(uint index, out IntPtr device);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int NvmlDeviceGetMemoryInfoDelegate(IntPtr device, out NvmlMemory memory);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int NvmlDeviceGetTemperatureDelegate(IntPtr device, int sensorType, out uint temp);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int NvmlDeviceGetUtilizationRatesDelegate(IntPtr device, out NvmlUtilization utilization);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int NvmlShutdownDelegate();

        public static readonly NvmlInitDelegate NvmlInit;
        public static readonly NvmlDeviceGetHandleByIndexDelegate NvmlDeviceGetHandleByIndex;
        public static readonly NvmlDeviceGetMemoryInfoDelegate NvmlDeviceGetMemoryInfo;
        public static readonly NvmlDeviceGetTemperatureDelegate NvmlDeviceGetTemperature;
        public static readonly NvmlDeviceGetUtilizationRatesDelegate NvmlDeviceGetUtilizationRates;
        public static readonly NvmlShutdownDelegate NvmlShutdown;

        static NvmlApi()
        {
            string[] paths = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? new[] { "nvml.dll" }
                : new[] { "libnvidia-ml.so.1", "/usr/local/nvidia/lib64/libnvidia-ml.so.1" };

            IntPtr handle = IntPtr.Zero;
            foreach (var path in paths)
            {
                if (NativeLibrary.TryLoad(path, out handle)) break;
            }

            if (handle == IntPtr.Zero)
                throw new DllNotFoundException("Could not load NVML library.");

            _libHandle = handle;

            NvmlInit = GetDelegate<NvmlInitDelegate>("nvmlInit_v2");
            NvmlDeviceGetHandleByIndex = GetDelegate<NvmlDeviceGetHandleByIndexDelegate>("nvmlDeviceGetHandleByIndex_v2");
            NvmlDeviceGetMemoryInfo = GetDelegate<NvmlDeviceGetMemoryInfoDelegate>("nvmlDeviceGetMemoryInfo");
            NvmlDeviceGetTemperature = GetDelegate<NvmlDeviceGetTemperatureDelegate>("nvmlDeviceGetTemperature");
            NvmlDeviceGetUtilizationRates = GetDelegate<NvmlDeviceGetUtilizationRatesDelegate>("nvmlDeviceGetUtilizationRates");
            NvmlShutdown = GetDelegate<NvmlShutdownDelegate>("nvmlShutdown");
        }

        private static T GetDelegate<T>(string entryPoint)
        {
            IntPtr addr = NativeLibrary.GetExport(_libHandle, entryPoint);
            return Marshal.GetDelegateForFunctionPointer<T>(addr);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NvmlUtilization
    {
        public uint Gpu;
        public uint Memory;
    }
}