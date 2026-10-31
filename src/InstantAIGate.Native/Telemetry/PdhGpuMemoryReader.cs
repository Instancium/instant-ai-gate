namespace InstantAIGate.Native.Telemetry;

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

/// <summary>
/// Reads device-wide dedicated GPU usage through the Windows PDH subsystem
/// ("GPU Adapter Memory(*)\Dedicated Usage"), which is vendor-neutral
/// (NVIDIA/AMD/Intel) and requires no proprietary SDKs.
/// </summary>
/// <remarks>
/// Wildcard expansion and counter creation can take tens of milliseconds, so
/// initialization runs on a background task. Until it completes (or if the
/// category is unavailable, e.g. inside containers) <c>TryGetDeviceDedicatedUsageBytes</c>
/// returns false and callers degrade to DXGI process-attributed usage.
/// </remarks>
internal sealed class PdhGpuMemoryReader : IDisposable
{
    private const string DeviceUsageCounterPath = "\\GPU Adapter Memory(*)\\Dedicated Usage";
    private const int PdhFmtLarge = 0x00000400;
    private const int PdhFmtNoScale = 0x00001000;

    private readonly object _syncLock = new();
    private readonly List<IntPtr> _counters = new();
    private IntPtr _query;
    private volatile bool _isReady;
    private bool _disposed;

    public PdhGpuMemoryReader()
    {
        Task.Run(InitializeSafely);
    }

    public bool TryGetDeviceDedicatedUsageBytes(out long usageBytes)
    {
        usageBytes = 0;
        if (!_isReady || _disposed || !OperatingSystem.IsWindows())
        {
            return false;
        }

        lock (_syncLock)
        {
            if (!_isReady || _disposed || _query == IntPtr.Zero)
            {
                return false;
            }

            if (PdhCollectQueryData(_query) != 0)
            {
                _isReady = false;
                Task.Run(InitializeSafely);
                return false;
            }

            long sum = 0;
            foreach (IntPtr counter in _counters)
            {
                if (PdhGetFormattedCounterValue(counter, PdhFmtLarge | PdhFmtNoScale, out _, out PdhFormattedValue value) == 0 &&
                    value.CStatus == 0)
                {
                    sum += value.LargeValue;
                }
            }

            usageBytes = Math.Max(0, sum);
            return true;
        }
    }

    private void InitializeSafely()
    {
        try
        {
            Initialize();
        }
        catch
        {
            _isReady = false;
        }
    }

    private void Initialize()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        lock (_syncLock)
        {
            if (_disposed || _isReady)
            {
                return;
            }

            CloseUnsafe();
            if (PdhOpenQueryW(null, 0, out _query) != 0 || _query == IntPtr.Zero)
            {
                _query = IntPtr.Zero;
                return;
            }

            int bufferSize = 0;
            PdhExpandWildCardPathW(null, DeviceUsageCounterPath, null, ref bufferSize, 0);
            if (bufferSize <= 0)
            {
                CloseUnsafe();
                return;
            }

            char[] buffer = new char[bufferSize];
            if (PdhExpandWildCardPathW(null, DeviceUsageCounterPath, buffer, ref bufferSize, 0) != 0)
            {
                CloseUnsafe();
                return;
            }

            foreach (string counterPath in ParseMultiString(buffer))
            {
                if (PdhAddEnglishCounterW(_query, counterPath, 0, out IntPtr counter) == 0 && counter != IntPtr.Zero)
                {
                    _counters.Add(counter);
                }
            }

            if (_counters.Count == 0 || PdhCollectQueryData(_query) != 0)
            {
                CloseUnsafe();
                return;
            }

            _isReady = true;
        }
    }

    private void CloseUnsafe()
    {
        _counters.Clear();
        if (_query != IntPtr.Zero)
        {
            PdhCloseQuery(_query);
            _query = IntPtr.Zero;
        }

        _isReady = false;
    }

    private static List<string> ParseMultiString(char[] buffer)
    {
        var paths = new List<string>();
        var current = new StringBuilder();
        foreach (char c in buffer)
        {
            if (c == '\0')
            {
                if (current.Length == 0)
                {
                    break;
                }

                paths.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        return paths;
    }

    public void Dispose()
    {
        lock (_syncLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            CloseUnsafe();
        }
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct PdhFormattedValue
    {
        [FieldOffset(0)]
        public int CStatus;

        [FieldOffset(8)]
        public long LargeValue;
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int PdhOpenQueryW(string? szMachineName, nuint dwUserData, out IntPtr phQuery);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int PdhExpandWildCardPathW(
        string? szMachineName,
        string szWildCardPath,
        char[]? mszExpandedPathList,
        ref int pcchPathListLength,
        int dwFlags);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int PdhAddEnglishCounterW(IntPtr hQuery, string szFullCounterPath, nuint dwUserData, out IntPtr phCounter);

    [DllImport("pdh.dll", ExactSpelling = true)]
    private static extern int PdhCollectQueryData(IntPtr hQuery);

    [DllImport("pdh.dll", ExactSpelling = true)]
    private static extern int PdhGetFormattedCounterValue(
        IntPtr hCounter,
        int dwFormat,
        out int lpdwType,
        out PdhFormattedValue pValue);

    [DllImport("pdh.dll", ExactSpelling = true)]
    private static extern int PdhCloseQuery(IntPtr hQuery);
}
