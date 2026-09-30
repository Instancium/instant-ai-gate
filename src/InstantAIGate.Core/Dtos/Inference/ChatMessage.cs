namespace InstantAIGate.Core.Dtos.Inference;

using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
public record ChatMessage
{
    public string Role { get; init; }
    public IReadOnlyList<MessageContent> Parts { get; init; }
    public string Content => string.Join("\n", Parts.OfType<TextContent>().Select(p => p.Text));

    [JsonConstructor]
    public ChatMessage(string role, IReadOnlyList<MessageContent> parts)
    {
        Role = role;
        Parts = parts;
    }

    public ChatMessage(string role, string content)
    {
        Role = role;
        Parts = new List<MessageContent> { new TextContent(content) };
    }
}