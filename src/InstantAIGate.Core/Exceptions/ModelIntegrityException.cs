namespace InstantAIGate.Core.Exceptions;

using System;

public class ModelIntegrityException : Exception
{
    public ModelIntegrityException(string modelId, string filePath)
        : base($"Integrity validation failed for model '{modelId}' at path '{filePath}'. The file may be corrupted or incomplete.")
    {
    }
}