namespace InstantAIGate.Core.Dtos.Inference
{
    /// <summary>
    /// Represents image content located at a file system path.
    /// </summary>
    public record ImageFileContent(string FilePath) : MessageContent("image_file");
}
