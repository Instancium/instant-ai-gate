namespace InstantAIGate.Core.Dtos.Inference
{
    /// <summary>
    /// Represents plain text message content.
    /// </summary>
    public record TextContent(string Text) : MessageContent("text");
}
