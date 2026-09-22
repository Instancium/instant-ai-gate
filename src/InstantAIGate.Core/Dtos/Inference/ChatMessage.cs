namespace InstantAIGate.Core.Dtos.Inference;

using System.Collections.Generic;
using System.Linq;

public record ChatMessage
{
    public string Role { get; init; }

    public IReadOnlyList<MessageContent> Parts { get; init; }

    // Computed property for convenience of simple text clients
    public string Content => string.Join("\n", Parts.OfType<TextContent>().Select(p => p.Text));

    public ChatMessage(string role, IReadOnlyList<MessageContent> parts)
    {
        Role = role;
        Parts = parts;
    }

    // Kept solely as a shorthand for text-only messages
    public ChatMessage(string role, string content)
    {
        Role = role;
        Parts = new List<MessageContent> { new TextContent(content) };
    }
}