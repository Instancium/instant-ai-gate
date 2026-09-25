namespace InstantAIGate.Core.Interfaces.Inference;

/// <summary>
/// Provides model path resolution services.
/// </summary>
public interface IModelPathProvider
{
    /// <summary>
    /// Gets the full file path for the specified model repository.
    /// </summary>
    /// <param name="repoId">Model repository identifier.</param>
    /// <returns>Full file path to the model.</returns>
    Task<string> GetFullModelPathAsync(string repoId);
}