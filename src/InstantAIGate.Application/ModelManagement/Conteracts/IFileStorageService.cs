namespace InstantAIGate.Application.ModelManagement.Conteracts
{
    public interface IFileStorageService
    {
        void EnsureDirectoryExists(string path);
        bool FileExists(string path);
        long GetFileSize(string path);
        string GetTempPath(string destinationPath);
        void DeleteIfExists(string path);
        void MoveFileAtomic(string sourcePath, string destinationPath);
        void MoveDirectoryAtomic(string sourceDirectoryPath, string destinationDirectoryPath);
    }
}
