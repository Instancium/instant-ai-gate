// SPDX-FileCopyrightText: (c) InstantAI Gate Contributors
// SPDX-License-Identifier: MIT

using System;
using System.Runtime.InteropServices;

namespace InstantAIGate.Native.Bindings;

/// <summary>
/// Input chunk types for multimodal processing.
/// </summary>
public enum MtmdInputChunkType
{
    Text = 0,
    Image = 1,
    Audio = 2,
    Count = 3,
}

/// <summary>
/// Parameters for mtmd context initialization.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct MtmdContextParams
{
    [MarshalAs(UnmanagedType.I1)]
    public bool UseGpu;
    public IntPtr Device;
    [MarshalAs(UnmanagedType.I1)]
    public bool PrintTimings;
    public int NThreads;
    public IntPtr ImageMarker;
    public IntPtr MediaMarker;
    public LlamaFlashAttnType FlashAttnType;
    [MarshalAs(UnmanagedType.I1)]
    public bool Warmup;
    public int ImageMinTokens;
    public int ImageMaxTokens;
    public IntPtr CbEval;
    public IntPtr CbEvalUserData;
    public int BatchMaxTokens;
    public IntPtr ProgressCallback;
    public IntPtr ProgressCallbackUserData;
}

/// <summary>
/// Decoder position structure for M-RoPE models.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct MtmdDecoderPos
{
    public uint T;
    public uint X;
    public uint Y;
    public uint Z;
}

/// <summary>
/// Input text structure for tokenization.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct MtmdInputText
{
    public IntPtr Text;
    public nuint TextLen;
    [MarshalAs(UnmanagedType.I1)]
    public bool AddSpecial;
    [MarshalAs(UnmanagedType.I1)]
    public bool ParseSpecial;
}

/// <summary>
/// Input part structure containing either text or bitmap.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct MtmdInputPart
{
    public IntPtr Text;
    public IntPtr Bitmap;
}

/// <summary>
/// Callback delegate for progress reporting during mtmd loading.
/// </summary>
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
[return: MarshalAs(UnmanagedType.I1)]
public delegate bool MtmdProgressCallback(float progress, IntPtr userData);

/// <summary>
/// Callback delegate for lazy bitmap loading.
/// </summary>
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate int MtmdBitmapLazyCallback(
    nuint chunkIdx,
    IntPtr userData,
    out IntPtr outBitmap,
    out IntPtr outText);

/// <summary>
/// Opaque handle for mtmd context.
/// </summary>
public sealed class MtmdContext : SafeHandle
{
    public MtmdContext() : base(IntPtr.Zero, true) { }

    public override bool IsInvalid => handle == IntPtr.Zero;

    protected override bool ReleaseHandle()
    {
        return true;
    }
}

/// <summary>
/// Opaque handle for mtmd bitmap.
/// </summary>
public sealed class MtmdBitmap : SafeHandle
{
    public MtmdBitmap() : base(IntPtr.Zero, true) { }

    public override bool IsInvalid => handle == IntPtr.Zero;

    protected override bool ReleaseHandle()
    {
        return true;
    }
}

/// <summary>
/// Opaque handle for mtmd image tokens.
/// </summary>
public sealed class MtmdImageTokens : SafeHandle
{
    public MtmdImageTokens() : base(IntPtr.Zero, true) { }

    public override bool IsInvalid => handle == IntPtr.Zero;

    protected override bool ReleaseHandle()
    {
        return true;
    }
}

/// <summary>
/// Opaque handle for mtmd input chunk.
/// </summary>
public sealed class MtmdInputChunk : SafeHandle
{
    public MtmdInputChunk() : base(IntPtr.Zero, true) { }

    public override bool IsInvalid => handle == IntPtr.Zero;

    protected override bool ReleaseHandle()
    {
        return true;
    }
}

/// <summary>
/// Opaque handle for mtmd input chunks collection.
/// </summary>
public sealed class MtmdInputChunks : SafeHandle
{
    public MtmdInputChunks() : base(IntPtr.Zero, true) { }

    public override bool IsInvalid => handle == IntPtr.Zero;

    protected override bool ReleaseHandle()
    {
        return true;
    }
}

/// <summary>
/// Opaque handle for mtmd batch.
/// </summary>
public sealed class MtmdBatch : SafeHandle
{
    public MtmdBatch() : base(IntPtr.Zero, true) { }

    public override bool IsInvalid => handle == IntPtr.Zero;

    protected override bool ReleaseHandle()
    {
        return true;
    }
}
