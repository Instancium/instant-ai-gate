using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Server.Dtos.OpenAi;
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace InstantAIGate.Server.Mapping;

public static class ChatContractMapper
{
    public static ChatMessage ToDomain(this OpenAiChatMessageDto dto)
    {
        var parts = new List<MessageContent>();

        if (dto.Content is JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.String)
            {
                parts.Add(new TextContent(element.GetString() ?? string.Empty));
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                {
                    var type = item.GetProperty("type").GetString();
                    if (type == "text")
                    {
                        parts.Add(new TextContent(item.GetProperty("text").GetString() ?? string.Empty));
                    }
                    else if (type == "image_url")
                    {
                        var urlElement = item.GetProperty("image_url").GetProperty("url").GetString() ?? string.Empty;

                        if (urlElement.StartsWith("data:image", StringComparison.OrdinalIgnoreCase))
                        {
                            var base64Data = urlElement.Substring(urlElement.IndexOf(',') + 1);
                            // Ensure the domain contains ImageBase64Content abstraction
                            // fallback to ImageUrlContent if base64 domain is not strictly defined yet.
                            parts.Add(new ImageUrlContent(urlElement));
                        }
                        else
                        {
                            parts.Add(new ImageUrlContent(urlElement));
                        }
                    }
                }
            }
        }
        else if (dto.Content is string stringContent)
        {
            parts.Add(new TextContent(stringContent));
        }

        return new ChatMessage(dto.Role, parts);
    }
}