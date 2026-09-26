using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Server.Dtos.OpenAi;
using InstantAIGate.Server.Mapping;
using System.Text.Json;

namespace InstantAIGate.Core.Tests.Dtos;

public class OpenAiContractTests
{
    private readonly JsonSerializerOptions _jsonOptions;

    public OpenAiContractTests()
    {
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
    }

    [Fact]
    public void Should_Parse_And_Map_String_Content()
    {
        // Arrange
        string json = @"{""role"": ""user"", ""content"": ""Hello, AI!""}";

        // Act
        var dto = JsonSerializer.Deserialize<OpenAiChatMessageDto>(json, _jsonOptions);
        var domainMessage = dto!.ToDomain();

        // Assert
        Assert.NotNull(domainMessage);
        Assert.Equal("user", domainMessage.Role);
        Assert.Single(domainMessage.Parts);

        var textPart = domainMessage.Parts.First() as TextContent;
        Assert.NotNull(textPart);
        Assert.Equal("Hello, AI!", textPart.Text);
    }

    [Fact]
    public void Should_Parse_And_Map_Multimodal_Array_Content()
    {
        // Arrange
        string json = @"
        {
            ""role"": ""user"",
            ""content"": [
                { ""type"": ""text"", ""text"": ""Analyze this image:"" },
                { ""type"": ""image_url"", ""image_url"": { ""url"": ""https://example.com/test.jpg"" } },
                { ""type"": ""image_url"", ""image_url"": { ""url"": ""data:image/jpeg;base64,iVBORw0KGgo="" } }
            ]
        }";

        // Act
        var dto = JsonSerializer.Deserialize<OpenAiChatMessageDto>(json, _jsonOptions);
        var domainMessage = dto!.ToDomain();

        // Assert
        Assert.NotNull(domainMessage);
        Assert.Equal("user", domainMessage.Role);
        Assert.Equal(3, domainMessage.Parts.Count);

        // Validate text part
        var textPart = domainMessage.Parts[0] as TextContent;
        Assert.NotNull(textPart);
        Assert.Equal("Analyze this image:", textPart.Text);

        // Validate standard image URL
        var imageUrlPart = domainMessage.Parts[1] as ImageUrlContent;
        Assert.NotNull(imageUrlPart);
        Assert.Equal("https://example.com/test.jpg", imageUrlPart.Url);

        // Validate base64 image URL (mapped to ImageUrlContent in current domain layout)
        var base64Part = domainMessage.Parts[2] as ImageUrlContent;
        Assert.NotNull(base64Part);
        Assert.Equal("data:image/jpeg;base64,iVBORw0KGgo=", base64Part.Url);
    }
}