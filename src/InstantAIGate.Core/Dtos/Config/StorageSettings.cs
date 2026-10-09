namespace InstantAIGate.Core.Dtos.Config;

using System;
using System.IO;
using System.Runtime.InteropServices;

public record StorageSettings
{
    private string? _modelsDirectory;

    public string ModelsDirectory
    {
        get
        {
            string targetPath = _modelsDirectory ?? string.Empty;
            if (string.IsNullOrWhiteSpace(targetPath))
            {
                targetPath = GetDefaultPlatformModelsDirectory();
            }

            if (!Directory.Exists(targetPath))
            {
                Directory.CreateDirectory(targetPath);
            }

            return targetPath;
        }
        init => _modelsDirectory = value;
    }

    private static string GetDefaultPlatformModelsDirectory()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var commonAppData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            var basePath = Path.Combine(commonAppData, "Instancium", "InstantAIGate", "models");
            return basePath;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // System daemon storage under /var/lib, fallback to ~/.local/share if running non-elevated
            string basePath = Directory.Exists("/var/lib") && CanWriteToDirectory("/var/lib")
                ? Path.Combine("/var/lib", "instancium", "instantaigate", "models")
                : Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Instancium",
                    "InstantAIGate",
                    "models");

            return basePath;
        }

        // Fallback for macOS / other POSIX environments
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appData, "Instancium", "InstantAIGate", "models");
    }

    private static bool CanWriteToDirectory(string path)
    {
        try
        {
            string testFile = Path.Combine(path, $".probe_{Guid.NewGuid():N}");
            using (File.Create(testFile, 1, FileOptions.DeleteOnClose)) { }
            return true;
        }
        catch
        {
            return false;
        }
    }
}