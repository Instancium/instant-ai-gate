namespace InstantAIGate.Core.Dtos.Inference
{
    /// <summary>
    /// Represents a user or assistant message in the chat history. 
    /// </summary>
    public record ChatMessage(string Role, string Content);
}
