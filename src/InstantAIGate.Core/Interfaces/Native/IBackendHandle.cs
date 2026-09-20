// File: src/InstantAIGate.Core/Interfaces/Native/IBackendHandle.cs
namespace InstantAIGate.Core.Interfaces.Native
{
    using System;

    public interface IBackendHandle : IDisposable
    {
        string BackendType { get; }
    }

    public interface IModelHandle : IBackendHandle { }
    public interface IContextHandle : IBackendHandle { }
    public interface IVisionHandle : IBackendHandle { }
}