namespace InstantAIGate.Core.Interfaces.Inference;

/// <summary>
/// Resolves the effective embedding-capable model for embedding requests.
/// Single source of truth for embedding target resolution across
/// GatewayHub (remote plane) and LocalGatewayClient (local plane).
/// </summary>
public interface IEmbeddingModelResolver
{
    /// <summary>
    /// Resolves the target embedding model.
    /// When the requested model is active but not embedding-capable,
    /// auto-routes to a dedicated embedding model if one is loaded.
    /// </summary>
    /// <param name="requestedRepoId">Explicitly requested model id; may be null or empty for auto-resolution.</param>
    /// <returns>RepoId of an embedding-capable model.</returns>
    /// <exception cref="System.InvalidOperationException">
    /// Thrown when no embedding-capable model is available for the request.
    /// </exception>
    string Resolve(string? requestedRepoId);
}
