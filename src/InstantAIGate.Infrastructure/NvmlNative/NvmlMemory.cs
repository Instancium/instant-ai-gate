using System.Runtime.InteropServices;

namespace InstantAIGate.Infrastructure.NvmlNative
{
    [StructLayout(LayoutKind.Sequential)]
    public struct NvmlMemory
    {
        public ulong Total;
        public ulong Free;
        public ulong Used;
    }
}
