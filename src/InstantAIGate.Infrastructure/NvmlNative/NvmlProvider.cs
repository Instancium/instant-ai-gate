namespace InstantAIGate.Infrastructure.NvmlNative
{
    public class NvmlProvider : IDisposable
    {
        private readonly bool _initialized;

        public NvmlProvider()
        {
            try
            {
                _initialized = NvmlApi.NvmlInit() == 0;
            }
            catch
            {
                _initialized = false;
            }
        }

        public void Dispose()
        {
            if (_initialized)
            {
                try { NvmlApi.NvmlShutdown(); } catch { }
            }
        }

        public double GetUsedMemoryGb(uint index = 0)
        {
            if (_initialized && NvmlApi.NvmlDeviceGetHandleByIndex(index, out var device) == 0
                && NvmlApi.NvmlDeviceGetMemoryInfo(device, out var mem) == 0)
                return Math.Round(mem.Used / 1024.0 / 1024.0 / 1024.0, 2);
            return 0.0;
        }

        public double GetTotalMemoryGb(uint index = 0)
        {
            if (_initialized && NvmlApi.NvmlDeviceGetHandleByIndex(index, out var device) == 0
                && NvmlApi.NvmlDeviceGetMemoryInfo(device, out var mem) == 0)
                return Math.Round(mem.Total / 1024.0 / 1024.0 / 1024.0, 2);
            return 0.0;
        }

        public int GetTemperature(uint index = 0)
        {
            if (_initialized && NvmlApi.NvmlDeviceGetHandleByIndex(index, out var device) == 0
                && NvmlApi.NvmlDeviceGetTemperature(device, 0, out uint temp) == 0)
                return (int)temp;
            return 0;
        }

        public int GetUtilization(uint index = 0)
        {
            if (_initialized && NvmlApi.NvmlDeviceGetHandleByIndex(index, out var device) == 0
                && NvmlApi.NvmlDeviceGetUtilizationRates(device, out var util) == 0)
                return (int)util.Gpu;
            return 0;
        }
    }
}