namespace InstantAIGate.Core.Dtos.Status;

/// <summary>
/// Defines the operational lifecycle states of the AI Gateway.
/// </summary>
public enum GatewayOperationalStatus
{
    /// <summary>
    /// Gateway host is starting or no model lifecycle activity has begun.
    /// </summary>
    Uninitialized = 0,

    /// <summary>
    /// Model weights or vision projector assets are currently being downloaded over the network.
    /// </summary>
    ModelDownloading = 1,

    /// <summary>
    /// Model weights are being loaded into physical RAM/VRAM and native context is initializing.
    /// </summary>
    ModelLoading = 2,

    /// <summary>
    /// Model is fully loaded, contexts are initialized, and the gateway is ready for inference requests.
    /// </summary>
    Ready = 3,

    /// <summary>
    /// A fatal error occurred during model retrieval, verification, or native initialization.
    /// </summary>
    Faulted = 4
}