using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using System.Threading.Tasks;

namespace InstantAIGate.Server.Middleware;

public class PortRoutingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly int _publicPort;
    private readonly int _adminPort;

    public PortRoutingMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;
        _publicPort = ExtractPort(configuration, "PublicEndpoint", 5000);
        _adminPort = ExtractPort(configuration, "AdminEndpoint", 5001);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        int port = context.Connection.LocalPort;
        string path = context.Request.Path.Value ?? string.Empty;

        if (port == _publicPort && path.StartsWith("/admin", System.StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        if (port == _adminPort && path.StartsWith("/v1", System.StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        await _next(context);
    }

    private static int ExtractPort(IConfiguration config, string endpointName, int defaultPort)
    {
        var url = config[$"Kestrel:Endpoints:{endpointName}:Url"];
        if (string.IsNullOrWhiteSpace(url)) return defaultPort;

        var parts = url.Split(':');
        if (parts.Length > 0 && int.TryParse(parts[^1], out int port))
        {
            return port;
        }

        return defaultPort;
    }
}