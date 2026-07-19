using InstantAIGate.Domain.Entities;

namespace InstantAIGate.Application.Interfaces.Storage
{
    public interface IModelPathProvider
    {
        string GetModelDirectory(string repoId);
        string GetModelFilePath(string repoId, string fileName);
        Task<string> GetFullModelPathAsync(string repoId);
        Task<ModelManifest?> GetModelFromPathAsync(string fullPath);
    }
}
