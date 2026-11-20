namespace InstantAIGate.Client.Transport;

using InstantAIGate.Core.Interfaces.Hub;

/// <summary>
/// Centralizes SignalR hub method and client callback names to eliminate magic strings
/// and guarantee compile-time alignment with <see cref="IGatewayHub"/>.
/// </summary>
internal static class GatewayHubMethods
{
    internal const string JoinSession = nameof(IGatewayHub.JoinSession);
    internal const string LeaveSession = nameof(IGatewayHub.LeaveSession);
    internal const string DestroySession = nameof(IGatewayHub.DestroySession);
    internal const string SendPromptDelta = nameof(IGatewayHub.SendPromptDelta);
    internal const string RollbackSession = nameof(IGatewayHub.RollbackSession);
    internal const string ShiftSessionCache = nameof(IGatewayHub.ShiftSessionCache);
    internal const string GetSessionTokenCount = nameof(IGatewayHub.GetSessionTokenCount);
    internal const string GetGatewayStatus = nameof(IGatewayHub.GetGatewayStatus);
    internal const string GetActiveModelDetailsAsync = nameof(IGatewayHub.GetActiveModelDetailsAsync);
    internal const string GetLoadedModelsDetailsAsync = nameof(IGatewayHub.GetLoadedModelsDetailsAsync);
    internal const string GenerateEmbeddingsAsync = nameof(IGatewayHub.GenerateEmbeddingsAsync);
    internal const string GetEmbeddingDimensionAsync = nameof(IGatewayHub.GetEmbeddingDimensionAsync);

    internal const string ReceiveTokenDelta = "ReceiveTokenDelta";
    internal const string ReceiveError = "ReceiveError";
    internal const string ReceiveContextOverflow = "ReceiveContextOverflow";
    internal const string ReceiveSessionClosed = "ReceiveSessionClosed";
}
