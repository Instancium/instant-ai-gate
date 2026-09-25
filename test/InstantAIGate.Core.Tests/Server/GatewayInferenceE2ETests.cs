using InstantAIGate.Server.Dtos.OpenAi;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace InstantAIGate.Core.Tests.Server;

public class GatewayInferenceE2ETests : IClassFixture<GatewayTestFixture>
{
    private readonly HttpClient _publicClient;
    private readonly HttpClient _adminClient;
    private readonly string _testRepoId;

    public GatewayInferenceE2ETests(GatewayTestFixture fixture)
    {
        _publicClient = fixture.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://localhost:5000")
        });

        _adminClient = fixture.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://localhost:5001")
        });

        var config = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: true)
            .Build();

        _testRepoId = config["InstantAIGate:TestData:VisionRepoId"] ?? "qwen3-vl-8b-instruct";

        // Crucial: Load native libraries into the xUnit test process memory space
        // before the TestServer attempts to initialize the backend facade.
        InstantAIGate.Native.Bindings.NativeLibraryLoader.Load();
    }

    [Fact]
    public async Task Should_Load_Model_And_Generate_Completion_Via_Http()
    {
        // 1. Issue load command to the Admin API
        var loadRequest = new HttpRequestMessage(HttpMethod.Post, "/admin/models/load");
        loadRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "test-admin-secret");
        loadRequest.Content = JsonContent.Create(new { RepoId = _testRepoId });

        var loadResponse = await _adminClient.SendAsync(loadRequest);
        loadResponse.EnsureSuccessStatusCode();

        // 2. Verify the health check confirms the model is loaded into VRAM
        var healthResponse = await _publicClient.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, healthResponse.StatusCode);

        // 3. Issue a non-streaming chat completion request to the Public API
        var chatRequest = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions");
        chatRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "test-tenant-123");

        var payload = new ChatCompletionRequest(
            Model: _testRepoId,
            Messages: new List<OpenAiChatMessageDto>
            {
                new OpenAiChatMessageDto("user", "Respond with exactly one word: 'Acknowledged'.")
            },
            Temperature: 0.1f,
            TopP: null,
            MaxTokens: 10,
            Stream: false,
            Seed: 42
        );

        chatRequest.Content = JsonContent.Create(payload);
        var chatResponse = await _publicClient.SendAsync(chatRequest);

        chatResponse.EnsureSuccessStatusCode();
        var result = await chatResponse.Content.ReadFromJsonAsync<ChatCompletionResponse>();

        // 4. Validate the OpenAI-compliant JSON structure and content
        Assert.NotNull(result);
        Assert.Equal("chat.completion", result.ObjectType);
        Assert.Equal(_testRepoId, result.Model);
        Assert.Single(result.Choices);

        string generatedText = result.Choices[0].Message.Content;
        Assert.False(string.IsNullOrWhiteSpace(generatedText), "The model returned an empty string.");
    }

    [Fact]
    public async Task Should_Initiate_Download_And_Return_Accepted()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/admin/models/download");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "test-admin-secret");
        request.Content = JsonContent.Create(new { RepoId = _testRepoId });

        var response = await _adminClient.SendAsync(request);

        // The controller should return 202 Accepted and execute the download in a background task
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }
}