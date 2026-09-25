using InstantAIGate.Core.Dtos.Config;
using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Interfaces.Inference;
using InstantAIGate.Server.Dtos.OpenAi;
using InstantAIGate.Server.Mapping;
using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Text.Json;

namespace InstantAIGate.Server.Controllers.v1;

[ApiController]
[Route("v1/chat/completions")]
public class ChatCompletionsController : ControllerBase
{
    private readonly IModelManager _modelManager;
    private readonly IInferenceEngine _inferenceEngine;
    private readonly IQueueManager _queueManager;

    public ChatCompletionsController(
        IModelManager modelManager,
        IInferenceEngine inferenceEngine,
        IQueueManager queueManager)
    {
        _modelManager = modelManager;
        _inferenceEngine = inferenceEngine;
        _queueManager = queueManager;
    }

    [HttpPost]
    public async Task<IActionResult> CreateChatCompletion([FromBody] ChatCompletionRequest request)
    {
        // 1. Identify Tenant and Active Model
        string tenantId = User.FindFirst("TenantId")?.Value ?? "anonymous";
        var activeConfig = _modelManager.GetActiveSettings();

        if (activeConfig == null)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                error = new { message = "No model is currently loaded.", type = "server_error" }
            });
        }

        if (!string.IsNullOrWhiteSpace(request.Model) &&
            !request.Model.Equals(activeConfig.RepoId, StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new
            {
                error = new { message = $"Requested model '{request.Model}' is not active. Active model is '{activeConfig.RepoId}'.", type = "invalid_request_error" }
            });
        }

        // 2. Queue Lease Acquisition (Backpressure control)
        // This will throw QueueFullException or TimeoutException which are handled by GatewayExceptionMiddleware
        using var lease = await _queueManager.EnqueueRequestAsync(
            tenantId,
            TimeSpan.FromSeconds(30),
            HttpContext.RequestAborted);

        // 3. Contract Mapping & Formatting
        var domainMessages = request.Messages.Select(m => m.ToDomain()).ToList();
        var mediaParts = domainMessages
            .SelectMany(m => m.Parts)
            .Where(p => p is not TextContent)
            .ToList();

        string prompt = await _inferenceEngine.ApplyChatTemplateAsync(
            activeConfig.RepoId,
            domainMessages,
            HttpContext.RequestAborted);

        var settings = new InferenceSettings
        {
            Temperature = request.Temperature ?? 0.7f,
            TopP = request.TopP ?? 0.9f,
            MaxTokens = request.MaxTokens ?? 2048,
            Seed = request.Seed
        };

        string responseId = $"chatcmpl-{Guid.NewGuid():N}";
        long createdUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // 4. Execution Mode Branching
        if (request.Stream)
        {
            return await ExecuteStreamingAsync(activeConfig.RepoId, prompt, mediaParts, settings, responseId, createdUnix);
        }
        else
        {
            return await ExecuteNonStreamingAsync(activeConfig.RepoId, prompt, mediaParts, settings, responseId, createdUnix);
        }
    }

    private async Task<IActionResult> ExecuteNonStreamingAsync(
        string repoId,
        string prompt,
        IReadOnlyList<MessageContent> mediaParts,
        InferenceSettings settings,
        string responseId,
        long createdUnix)
    {
        var sb = new StringBuilder();

        await foreach (var chunk in _inferenceEngine.StreamGenerationAsync(repoId, prompt, mediaParts, settings, HttpContext.RequestAborted))
        {
            sb.Append(chunk);
        }

        var response = new ChatCompletionResponse(
            Id: responseId,
            ObjectType: "chat.completion",
            Created: createdUnix,
            Model: repoId,
            Choices: new List<ChatChoice>
            {
                new ChatChoice(
                    Index: 0,
                    Message: new OpenAiMessageResponse("assistant", sb.ToString()),
                    FinishReason: "stop"
                )
            }
        );

        return Ok(response);
    }

    private async Task<IActionResult> ExecuteStreamingAsync(
        string repoId,
        string prompt,
        IReadOnlyList<MessageContent> mediaParts,
        InferenceSettings settings,
        string responseId,
        long createdUnix)
    {
        Response.Headers.Append("Content-Type", "text/event-stream");
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("Connection", "keep-alive");
        Response.Headers.Append("X-Accel-Buffering", "no"); // Disable NGINX buffering if present

        var jsonOptions = new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

        // Send initial role chunk
        var initialChunk = new ChatCompletionChunk(
            Id: responseId,
            ObjectType: "chat.completion.chunk",
            Created: createdUnix,
            Model: repoId,
            Choices: new List<ChunkChoice>
            {
                new ChunkChoice(Index: 0, Delta: new ChunkDelta(Role: "assistant"), FinishReason: null)
            }
        );

        await Response.WriteAsync($"data: {JsonSerializer.Serialize(initialChunk, jsonOptions)}\n\n", HttpContext.RequestAborted);
        await Response.Body.FlushAsync(HttpContext.RequestAborted);

        // Stream generated tokens
        await foreach (var token in _inferenceEngine.StreamGenerationAsync(repoId, prompt, mediaParts, settings, HttpContext.RequestAborted))
        {
            var chunk = new ChatCompletionChunk(
                Id: responseId,
                ObjectType: "chat.completion.chunk",
                Created: createdUnix,
                Model: repoId,
                Choices: new List<ChunkChoice>
                {
                    new ChunkChoice(Index: 0, Delta: new ChunkDelta(Content: token), FinishReason: null)
                }
            );

            await Response.WriteAsync($"data: {JsonSerializer.Serialize(chunk, jsonOptions)}\n\n", HttpContext.RequestAborted);
            await Response.Body.FlushAsync(HttpContext.RequestAborted);
        }

        // Send terminal chunk
        var finalChunk = new ChatCompletionChunk(
            Id: responseId,
            ObjectType: "chat.completion.chunk",
            Created: createdUnix,
            Model: repoId,
            Choices: new List<ChunkChoice>
            {
                new ChunkChoice(Index: 0, Delta: new ChunkDelta(), FinishReason: "stop")
            }
        );

        await Response.WriteAsync($"data: {JsonSerializer.Serialize(finalChunk, jsonOptions)}\n\n", HttpContext.RequestAborted);
        await Response.WriteAsync("data: [DONE]\n\n", HttpContext.RequestAborted);
        await Response.Body.FlushAsync(HttpContext.RequestAborted);

        return new EmptyResult();
    }
}