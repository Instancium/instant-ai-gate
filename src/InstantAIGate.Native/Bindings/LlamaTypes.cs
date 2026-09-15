// SPDX-FileCopyrightText: (c) InstantAI Gate Contributors
// SPDX-License-Identifier: MIT

using System;
using System.Runtime.InteropServices;

namespace InstantAIGate.Native.Bindings;

/// <summary>
/// Represents a token ID in the llama vocabulary.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct LlamaToken
{
    public int Id;
}

/// <summary>
/// Vocabulary types supported by llama.cpp.
/// </summary>
public enum LlamaVocabType
{
    None = 0,
    Spm = 1,
    Bpe = 2,
    Wpm = 3,
    Ugm = 4,
    Rwkv = 5,
    Plamo2 = 6,
}

/// <summary>
/// RoPE (Rotary Positional Embeddings) types.
/// </summary>
public enum LlamaRopeType
{
    None = -1,
    Norm = 0,
    Neox = GGML_ROPE_TYPE_NEOX,
    MRope = GGML_ROPE_TYPE_MROPE,
    IMRope = GGML_ROPE_TYPE_IMROPE,
    Vision = GGML_ROPE_TYPE_VISION,
}

internal static class GGML_Rope_Types
{
    public const int GGML_ROPE_TYPE_NEOX = 2;
    public const int GGML_ROPE_TYPE_MROPE = 4;
    public const int GGML_ROPE_TYPE_IMROPE = 6;
    public const int GGML_ROPE_TYPE_VISION = 8;
}

/// <summary>
/// Token type enumeration.
/// </summary>
[Flags]
public enum LlamaTokenType
{
    Undefined = 0,
    Unknown = 1 << 0,
    Unused = 1 << 1,
    Normal = 1 << 2,
    Control = 1 << 3,
    UserDefined = 1 << 4,
    Byte = 1 << 5,
}

/// <summary>
/// Token attributes flags.
/// </summary>
[Flags]
public enum LlamaTokenAttr
{
    Undefined = 0,
    Unknown = 1 << 0,
    Unused = 1 << 1,
    Normal = 1 << 2,
    Control = 1 << 3,
    UserDefined = 1 << 4,
    Byte = 1 << 5,
    Normalized = 1 << 6,
    LStrip = 1 << 7,
    RStrip = 1 << 8,
    SingleWord = 1 << 9,
}

/// <summary>
/// Model file types (quantization formats).
/// </summary>
public enum LlamaFType
{
    AllF32 = 0,
    MostlyF16 = 1,
    MostlyQ4_0 = 2,
    MostlyQ4_1 = 3,
    MostlyQ8_0 = 7,
    MostlyQ5_0 = 8,
    MostlyQ5_1 = 9,
    MostlyQ2_K = 10,
    MostlyQ3_K_S = 11,
    MostlyQ3_K_M = 12,
    MostlyQ3_K_L = 13,
    MostlyQ4_K_S = 14,
    MostlyQ4_K_M = 15,
    MostlyQ5_K_S = 16,
    MostlyQ5_K_M = 17,
    MostlyQ6_K = 18,
    MostlyIQ2_XXS = 19,
    MostlyIQ2_XS = 20,
    MostlyQ2_K_S = 21,
    MostlyIQ3_XS = 22,
    MostlyIQ3_XXS = 23,
    MostlyIQ1_S = 24,
    MostlyIQ4_NL = 25,
    MostlyIQ3_S = 26,
    MostlyIQ3_M = 27,
    MostlyIQ2_S = 28,
    MostlyIQ2_M = 29,
    MostlyIQ4_XS = 30,
    MostlyIQ1_M = 31,
    MostlyBF16 = 32,
    MostlyTQ1_0 = 36,
    MostlyTQ2_0 = 37,
    MostlyMXFP4_MOE = 38,
    MostlyNVFP4 = 39,
    MostlyQ1_0 = 40,
    MostlyQ2_0 = 41,
    Guessed = 1024,
}

/// <summary>
/// RoPE scaling types.
/// </summary>
public enum LlamaRopeScalingType
{
    Unspecified = -1,
    None = 0,
    Linear = 1,
    Yarn = 2,
    LongRope = 3,
    MaxValue = LongRope,
}

/// <summary>
/// Pooling types for sequence embeddings.
/// </summary>
public enum LlamaPoolingType
{
    Unspecified = -1,
    None = 0,
    Mean = 1,
    Cls = 2,
    Last = 3,
    Rank = 4,
}

/// <summary>
/// Attention types.
/// </summary>
public enum LlamaAttentionType
{
    Unspecified = -1,
    Causal = 0,
    NonCausal = 1,
}

/// <summary>
/// Flash attention modes.
/// </summary>
public enum LlamaFlashAttnType
{
    Auto = -1,
    Disabled = 0,
    Enabled = 1,
}

/// <summary>
/// Model split modes for multi-GPU configurations.
/// </summary>
public enum LlamaSplitMode
{
    None = 0,
    Layer = 1,
    Row = 2,
    Tensor = 3,
}

/// <summary>
/// Model loading modes.
/// </summary>
public enum LlamaLoadMode
{
    Auto = -1,
    None = 0,
    MMap = 1,
    MLock = 2,
    MMapMLock = 3,
    DirectIO = 4,
}

/// <summary>
/// Lazy loading modes for tensors.
/// </summary>
public enum LlamaLazyMode
{
    Off = 0,
    Auto = 1,
    On = 2,
}

/// <summary>
/// Context types.
/// </summary>
public enum LlamaContextType
{
    Default = 0,
    Mtp = 1,
}

/// <summary>
/// Token data structure for sampling.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct LlamaTokenData
{
    public int Id;
    public float Logit;
    public float P;
}

/// <summary>
/// Array of token data with sorting information.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct LlamaTokenDataArray
{
    public IntPtr Data;
    public nuint Size;
    public long Selected;
    [MarshalAs(UnmanagedType.I1)]
    public bool Sorted;
}

/// <summary>
/// Callback delegate for progress reporting during model loading.
/// </summary>
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
[return: MarshalAs(UnmanagedType.I1)]
public delegate bool LlamaProgressCallback(float progress, IntPtr userData);

/// <summary>
/// Batch structure for input tokens to llama_encode/llama_decode.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct LlamaBatch
{
    public int NTokens;
    public IntPtr Token;
    public IntPtr Embd;
    public IntPtr Pos;
    public IntPtr NSeqId;
    public IntPtr SeqId;
    public IntPtr Logits;
}

/// <summary>
/// Key-value override types for model metadata.
/// </summary>
public enum LlamaModelKvOverrideType
{
    Int,
    Float,
    Bool,
    Str,
}

/// <summary>
/// Model metadata keys that can be overridden.
/// </summary>
public enum LlamaModelMetaKey
{
    SamplingSequence,
    SamplingTopK,
    SamplingTopP,
    SamplingMinP,
    SamplingXtcProbability,
    SamplingXtcThreshold,
    SamplingTemp,
    SamplingPenaltyLastN,
    SamplingPenaltyRepeat,
    SamplingMirostat,
    SamplingMirostatTau,
    SamplingMirostatEta,
}

/// <summary>
/// Key-value override structure for model parameters.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 264)]
public struct LlamaModelKvOverride
{
    [FieldOffset(0)]
    public LlamaModelKvOverrideType Tag;

    [FieldOffset(8)]
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    public string Key;

    [FieldOffset(136)]
    public long ValI64;

    [FieldOffset(136)]
    public double ValF64;

    [FieldOffset(136)]
    [MarshalAs(UnmanagedType.I1)]
    public bool ValBool;

    [FieldOffset(136)]
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    public string ValStr;
}

/// <summary>
/// Buffer type override for specific model tensors.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct LlamaModelTensorBuftOverride
{
    public IntPtr Pattern;
    public IntPtr Buft;
}

/// <summary>
/// Parameters for model initialization.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct LlamaModelParams
{
    public IntPtr Devices;
    public IntPtr TensorBuftOverrides;
    public int NGpuLayers;
    public LlamaSplitMode SplitMode;
    public LlamaLoadMode LoadMode;
    public LlamaLazyMode LazyMode;
    public int MainGpu;
    public IntPtr TensorSplit;
    public IntPtr ProgressCallback;
    public IntPtr ProgressCallbackUserData;
    public IntPtr KvOverrides;
    [MarshalAs(UnmanagedType.I1)]
    public bool VocabOnly;
    [MarshalAs(UnmanagedType.I1)]
    public bool CheckTensors;
    [MarshalAs(UnmanagedType.I1)]
    public bool UseExtraBufts;
    [MarshalAs(UnmanagedType.I1)]
    public bool NoHost;
    [MarshalAs(UnmanagedType.I1)]
    public bool NoAlloc;
    [MarshalAs(UnmanagedType.I1)]
    public bool LoadMtp;
}

/// <summary>
/// Sampler sequence configuration.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct LlamaSamplerSeqConfig
{
    public int SeqId;
    public IntPtr Sampler;
}

/// <summary>
/// Parameters for context initialization.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct LlamaContextParams
{
    public uint NCtx;
    public uint NBatch;
    public uint NUbatch;
    public uint NSeqMax;
    public uint NRsSeq;
    public uint NOutputsMax;
    public uint NOutputsMaxPerSeq;
    public int NThreads;
    public int NThreadsBatch;
    public LlamaContextType CtxType;
    public LlamaPoolingType PoolingType;
    public LlamaAttentionType AttentionType;
    public LlamaFlashAttnType FlashAttnType;
    [MarshalAs(UnmanagedType.I1)]
    public bool Embeddings;
    [MarshalAs(UnmanagedType.I1)]
    public bool OffloadKqv;
    [MarshalAs(UnmanagedType.I1)]
    public bool FlashAttn;
    [MarshalAs(UnmanagedType.I1)]
    public bool NoPerLayerMutableState;
    public IntPtr Schedulers;
    public IntPtr OpOverrides;
}

/// <summary>
/// Parameters for sampler chain initialization.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct LlamaSamplerChainParams
{
    [MarshalAs(UnmanagedType.I1)]
    public bool NoPerf;
}

/// <summary>
/// Parameters for model quantization.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct LlamaModelQuantizeParams
{
    public int NThread;
    public LlamaFType QuantizeOutputType;
    public IntPtr AllowRequantize;
    public IntPtr QuantizeImatrix;
    [MarshalAs(UnmanagedType.I1)]
    public bool Pure;
    [MarshalAs(UnmanagedType.I1)]
    public bool SplitMode;
}

/// <summary>
/// Opaque handle for llama vocabulary.
/// </summary>
public sealed class LlamaVocab : SafeHandle
{
    public LlamaVocab() : base(IntPtr.Zero, true) { }

    public override bool IsInvalid => handle == IntPtr.Zero;

    protected override bool ReleaseHandle()
    {
        return true;
    }
}

/// <summary>
/// Opaque handle for llama model.
/// </summary>
public sealed class LlamaModel : SafeHandle
{
    public LlamaModel() : base(IntPtr.Zero, true) { }

    public override bool IsInvalid => handle == IntPtr.Zero;

    protected override bool ReleaseHandle()
    {
        return true;
    }
}

/// <summary>
/// Opaque handle for llama context.
/// </summary>
public sealed class LlamaContext : SafeHandle
{
    public LlamaContext() : base(IntPtr.Zero, true) { }

    public override bool IsInvalid => handle == IntPtr.Zero;

    protected override bool ReleaseHandle()
    {
        return true;
    }
}

/// <summary>
/// Opaque handle for llama sampler.
/// </summary>
public sealed class LlamaSampler : SafeHandle
{
    public LlamaSampler() : base(IntPtr.Zero, true) { }

    public override bool IsInvalid => handle == IntPtr.Zero;

    protected override bool ReleaseHandle()
    {
        return true;
    }
}

/// <summary>
/// Opaque handle for llama memory.
/// </summary>
public sealed class LlamaMemory : SafeHandle
{
    public LlamaMemory() : base(IntPtr.Zero, true) { }

    public override bool IsInvalid => handle == IntPtr.Zero;

    protected override bool ReleaseHandle()
    {
        return true;
    }
}
