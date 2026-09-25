namespace InstantAIGate.Core.Dtos.Inference
{
    public record ImageFileContent(string FilePath) : MessageContent("image_file");
}
