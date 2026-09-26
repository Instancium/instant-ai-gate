namespace InstantAIGate.Core.Dtos.Config;

using System;
using System.IO;

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
                // Fallback to CommonApplicationData
                var commonAppData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
                targetPath = Path.Combine(commonAppData, "InstantAIGate", "models");
            }

            if (!Directory.Exists(targetPath))
            {
                Directory.CreateDirectory(targetPath);
            }

            return targetPath;
        }
        init => _modelsDirectory = value;
    }
}