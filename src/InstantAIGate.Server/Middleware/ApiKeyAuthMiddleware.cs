using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using System.Security.Claims;
using System.Threading.Tasks;

namespace InstantAIGate.Server.Middleware;

public class ApiKeyAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string _adminApiKey;
    private readonly int _publicPort;
    private readonly int _adminPort;

    public ApiKeyAuthMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;
        _adminApiKey = configuration["InstantAIGate:AdminApiKey"] ?? string.Empty;
        _publicPort = ExtractPort(configuration, "PublicEndpoint", 5000);
        _adminPort = ExtractPort(configuration, "AdminEndpoint", 5001);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        string path = context.Request.Path.Value ?? string.Empty;

        // Bypass authentication for health checks
        if (path.StartsWith("/health", System.StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        int port = context.Connection.LocalPort;

        if (!context.Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        string token = authHeader.ToString().Replace("Bearer ", string.Empty).Trim();

        if (port == _adminPort)
        {
            if (string.IsNullOrEmpty(_adminApiKey) || token != _adminApiKey)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            var claims = new[] { new Claim(ClaimTypes.Role, "Admin") };
            var identity = new ClaimsIdentity(claims, "ApiKey");
            context.User = new ClaimsPrincipal(identity);
        }
        else if (port == _publicPort)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            var claims = new[] { new Claim("TenantId", token) };
            var identity = new ClaimsIdentity(claims, "ApiKey");
            context.User = new ClaimsPrincipal(identity);
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