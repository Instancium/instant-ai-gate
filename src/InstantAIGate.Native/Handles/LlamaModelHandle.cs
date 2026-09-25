// File: src/InstantAIGate.Native/Handles/NativeHandles.cs
namespace InstantAIGate.Native.Handles;

using InstantAIGate.Core.Interfaces.Native;
using System;

internal sealed class LlamaModelHandle : IModelHandle
{
    public IntPtr Pointer { get; }
    public string BackendType => "llama.cpp";

    public LlamaModelHandle(IntPtr pointer)
    {
        Pointer = pointer != IntPtr.Zero ? pointer : throw new ArgumentNullException(nameof(pointer));
    }

    public void Dispose() { /* Cleanup managed by Facade */ }
}

internal sealed class LlamaContextHandle : IContextHandle
{
    public IntPtr Pointer { get; }
    public string BackendType => "llama.cpp";

    public LlamaContextHandle(IntPtr pointer)
    {
        Pointer = pointer != IntPtr.Zero ? pointer : throw new ArgumentNullException(nameof(pointer));
    }

    public void Dispose() { /* Cleanup managed by Facade */ }
}

internal sealed class MtmdVisionHandle : IVisionHandle
{
    public IntPtr Pointer { get; }
    public string BackendType => "llama.cpp-clip";

    public MtmdVisionHandle(IntPtr pointer)
    {
        Pointer = pointer != IntPtr.Zero ? pointer : throw new ArgumentNullException(nameof(pointer));
    }

    public void Dispose() { /* Cleanup managed by Facade */ }
}