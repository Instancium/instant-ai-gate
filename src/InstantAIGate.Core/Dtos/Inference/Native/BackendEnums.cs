using System;
using System.Collections.Generic;
using System.Text;

namespace InstantAIGate.Core.Dtos.Inference.Native
{
    /// <summary>
    /// Flash attention mode.
    /// </summary>
    public enum BackendFlashAttentionType
    {
        /// <summary>
        /// Flash attention disabled.
        /// </summary>
        Disabled = 0,

        /// <summary>
        /// Flash attention enabled.
        /// </summary>
        Enabled = 1,

        /// <summary>
        /// Auto-detect flash attention support.
        /// </summary>
        Auto = 2
    }

    /// <summary>
    /// KV cache quantization type.
    /// </summary>
    public enum BackendKvCacheType
    {
        /// <summary>
        /// 32-bit floating point.
        /// </summary>
        F32 = 0,

        /// <summary>
        /// 16-bit floating point.
        /// </summary>
        F16 = 1,

        /// <summary>
        /// 8-bit quantized.
        /// </summary>
        Q8_0 = 2,

        /// <summary>
        /// 4-bit quantized (original).
        /// </summary>
        Q4_0 = 3,

        /// <summary>
        /// 4-bit quantized (K-quants).
        /// </summary>
        Q4_K = 4,

        /// <summary>
        /// 5-bit quantized (K-quants).
        /// </summary>
        Q5_K = 5,

        /// <summary>
        /// 8-bit quantized (K-quants).
        /// </summary>
        Q8_K = 6
    }



    /// <summary>
    /// Log level for backend operations.
    /// </summary>
    public enum BackendLogLevel
    {
        /// <summary>
        /// Debug level logging.
        /// </summary>
        Debug = 0,

        /// <summary>
        /// Information level logging.
        /// </summary>
        Info = 1,

        /// <summary>
        /// Warning level logging.
        /// </summary>
        Warning = 2,

        /// <summary>
        /// Error level logging.
        /// </summary>
        Error = 3
    }

    /// <summary>
    /// Layer split mode for GPU offloading.
    /// </summary>
    public enum BackendSplitMode
    {
        /// <summary>
        /// No splitting.
        /// </summary>
        None = 0,

        /// <summary>
        /// Split layers across GPUs.
        /// </summary>
        Layer = 1,

        /// <summary>
        /// Split rows across GPUs.
        /// </summary>
        Row = 2
    }

    /// <summary>
    /// Native logging callback delegate.
    /// </summary>
    public delegate void BackendLogCallback(BackendLogLevel level, string message);

}
