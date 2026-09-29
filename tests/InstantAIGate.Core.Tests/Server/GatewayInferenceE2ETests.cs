using InstantAIGate.Core.Tests.TestConfiguration;
using InstantAIGate.Server.Dtos.OpenAi;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace InstantAIGate.Core.Tests.Server;

public class GatewayInferenceE2ETests : IClassFixture<GatewayTestFixture>
{
    private readonly HttpClient _publicClient;
    private readonly HttpClient _adminClient;
    private readonly TestServerOptions _serverOptions;
    private readonly TestInferenceRequestOptions _inferenceOptions;

    // Target model identifier comes from the test project appsettings.json.
    private readonly string _testRepoId;

    public GatewayInferenceE2ETests(GatewayTestFixture fixture)
    {
        _serverOptions = fixture.ServerOptions;
        _inferenceOptions = fixture.ModelOptions.Inference;
        _testRepoId = fixture.ModelOptions.RepoId;

        // Base addresses are provided by the fixture helpers (ports come from config).
        _publicClient = fixture.CreatePublicClient();
        _adminClient = fixture.CreateAdminClient();

        // Native libraries must be loaded into the test process memory space
        // before the TestServer initializes the backend facade.
        InstantAIGate.Native.Bindings.NativeLibraryLoader.Load();
    }

    [Fact]
    public async Task Should_Load_Model_And_Generate_Completion_Via_Http()
    {
        // 1. Issue load command to the Admin API
        var loadRequest = new HttpRequestMessage(HttpMethod.Post, "/admin/models/load");
        loadRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _serverOptions.AdminApiKey);
        loadRequest.Content = JsonContent.Create(new { RepoId = _testRepoId });

        var loadResponse = await _adminClient.SendAsync(loadRequest);
        loadResponse.EnsureSuccessStatusCode();

        // 2. Verify the health check confirms the model is loaded into VRAM
        var healthResponse = await _publicClient.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, healthResponse.StatusCode);

        // 3. Issue a non-streaming chat completion request to the Public API
        var chatRequest = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions");
        chatRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _serverOptions.TenantApiKey);

        var payload = new ChatCompletionRequest(
            Model: _testRepoId,
            Messages: new List<OpenAiChatMessageDto>
            {
                new OpenAiChatMessageDto("user", _inferenceOptions.E2ePrompt)
            },
            Temperature: _inferenceOptions.E2eTemperature,
            TopP: null,
            MaxTokens: _inferenceOptions.E2eMaxTokens,
            Stream: false,
            Seed: (uint)_inferenceOptions.Seed
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
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _serverOptions.AdminApiKey);
        request.Content = JsonContent.Create(new { RepoId = _testRepoId });

        var response = await _adminClient.SendAsync(request);

        // The controller should return 202 Accepted and execute the download in a background task
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }
}
