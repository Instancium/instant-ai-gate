using InstantAIGate.Core.Exceptions;
using System.Text.Json;

namespace InstantAIGate.Server.Middleware;

public class GatewayExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GatewayExceptionMiddleware> _logger;

    public GatewayExceptionMiddleware(RequestDelegate next, ILogger<GatewayExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (QueueFullException ex)
        {
            _logger.LogWarning(ex, "Request rejected. Queue capacity reached.");
            await WriteErrorResponseAsync(context, StatusCodes.Status429TooManyRequests, ex.Message);
        }
        catch (TimeoutException ex)
        {
            _logger.LogWarning(ex, "Request timed out waiting for inference execution slot.");
            await WriteErrorResponseAsync(context, StatusCodes.Status504GatewayTimeout, ex.Message);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("draining") || ex.Message.Contains("not active"))
        {
            _logger.LogWarning(ex, "Model state conflict detected.");
            await WriteErrorResponseAsync(context, StatusCodes.Status503ServiceUnavailable, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled internal gateway exception.");
            await WriteErrorResponseAsync(context, StatusCodes.Status500InternalServerError, "Internal Server Error");
        }
    }

    private static async Task WriteErrorResponseAsync(HttpContext context, int statusCode, string message)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";

        var errorResponse = new { error = new { message = message, type = "server_error" } };
        var payload = JsonSerializer.Serialize(errorResponse);

        await context.Response.WriteAsync(payload);
    }
}