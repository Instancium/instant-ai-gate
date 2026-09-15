namespace InstantAIGate.Cli.Services;

using System.IO;
using System.Threading.Tasks;
using InstantAIGate.Core.Interfaces.Inference;

/// <summary>
/// Resolves model paths from a local directory structure.
/// </summary>
public class LocalModelPathProvider : IModelPathProvider
{
    private readonly string _modelsDirectory;

    /// <summary>
    /// Initializes a new instance of the local model path provider.
    /// </summary>
    /// <param name="modelsDirectory">Base directory containing GGUF models.</param>
    public LocalModelPathProvider(string modelsDirectory)
    {
        _modelsDirectory = modelsDirectory;
        if (!Directory.Exists(_modelsDirectory))
        {
            Directory.CreateDirectory(_modelsDirectory);
        }
    }

    /// <summary>
    /// Gets the full file path for the specified model repository.
    /// </summary>
    public Task<string> GetFullModelPathAsync(string repoId)
    {
        string sanitizedId = repoId.Replace('/', '_').Replace('\\', '_');
        string fullPath = Path.Combine(_modelsDirectory, $"{sanitizedId}.gguf");
        return Task.FromResult(fullPath);
    }
}