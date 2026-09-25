using System.Text.Json.Serialization;

namespace InstantAIGate.Server.Dtos.OpenAi;

public record ChatCompletionChunk(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("object")] string ObjectType,
    [property: JsonPropertyName("created")] long Created,
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("choices")] List<ChunkChoice> Choices
);

public record ChunkChoice(
    [property: JsonPropertyName("index")] int Index,
    [property: JsonPropertyName("delta")] ChunkDelta Delta,
    [property: JsonPropertyName("finish_reason")] string? FinishReason
);

public record ChunkDelta(
    [property: JsonPropertyName("role")] string? Role = null,
    [property: JsonPropertyName("content")] string? Content = null
);