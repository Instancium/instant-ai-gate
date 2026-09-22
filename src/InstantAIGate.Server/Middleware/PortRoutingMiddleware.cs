using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;

namespace InstantAIGate.Server.Middleware;

public class PortRoutingMiddleware
{
    private readonly RequestDelegate _next;

    public PortRoutingMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        int port = context.Connection.LocalPort;
        string path = context.Request.Path.Value ?? string.Empty;

        if (port == 5000 && path.StartsWith("/admin", System.StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        if (port == 5001 && path.StartsWith("/v1", System.StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        await _next(context);
    }
}