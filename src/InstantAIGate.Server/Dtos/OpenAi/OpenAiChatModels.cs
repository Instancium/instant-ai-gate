using System.Text.Json.Serialization;

namespace InstantAIGate.Server.Dtos.OpenAi;

public record ChatCompletionRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("messages")] List<OpenAiChatMessageDto> Messages,
    [property: JsonPropertyName("temperature")] float? Temperature,
    [property: JsonPropertyName("top_p")] float? TopP,
    [property: JsonPropertyName("max_tokens")] int? MaxTokens,
    [property: JsonPropertyName("stream")] bool Stream = false,
    [property: JsonPropertyName("seed")] uint? Seed = null
);

public record OpenAiChatMessageDto(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] object Content
);

public record ChatCompletionResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("object")] string ObjectType,
    [property: JsonPropertyName("created")] long Created,
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("choices")] List<ChatChoice> Choices
);

public record ChatChoice(
    [property: JsonPropertyName("index")] int Index,
    [property: JsonPropertyName("message")] OpenAiMessageResponse Message,
    [property: JsonPropertyName("finish_reason")] string? FinishReason
);

public record OpenAiMessageResponse(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string Content
);