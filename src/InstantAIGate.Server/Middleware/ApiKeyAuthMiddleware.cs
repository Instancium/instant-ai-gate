using Microsoft.Extensions.Primitives;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace InstantAIGate.Server.Middleware;

public sealed class ApiKeyAuthMiddleware
{
    private const string AdminRole = "Admin";
    private const string UserRole = "User";
    private const string AuthenticationSchemeName = "ApiKey";

    private static readonly string[] AnonymousPathPrefixes = ["/_framework", "/css"];
    private static readonly string[] AnonymousExactPaths = ["/", "/index.html"];

    private readonly RequestDelegate _next;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ApiKeyAuthMiddleware> _logger;

    public ApiKeyAuthMiddleware(
        RequestDelegate next,
        IConfiguration configuration,
        ILogger<ApiKeyAuthMiddleware> logger)
    {
        _next = next;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        PathString path = context.Request.Path;

        if (IsHealthProbe(path) || IsAnonymousStaticAsset(context.Request))
        {
            await _next(context);
            return;
        }

        string? apiKey = ExtractApiKey(context.Request);

        if (apiKey is not null && TryResolveRole(apiKey, out string? role))
        {
            context.User = BuildPrincipal(role!);
            await _next(context);
            return;
        }

        _logger.LogWarning(
            "Rejected unauthenticated request {Method} {Path} from {RemoteIp}.",
            context.Request.Method,
            path,
            context.Connection.RemoteIpAddress);

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsync("API key is missing or invalid.");
    }

    private static ClaimsPrincipal BuildPrincipal(string role) =>
        new(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Role, role),
                new Claim(ClaimTypes.Name, role == AdminRole ? "gateway-admin" : "gateway-tenant")
            ],
            AuthenticationSchemeName));

    private static bool IsHealthProbe(PathString path)
        => path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Allow-list is deliberately restricted to GET/HEAD: a POST to the same paths must still demand
    /// a token, so the allow-list can never be abused as a generic auth bypass vector.
    /// Raw ordinal prefix matching is used intentionally: PathString.StartsWithSegments enforces
    /// segment boundaries and therefore cannot express trailing-slash prefixes ("/css/")
    /// or dotted prefixes ("/favicon.").
    /// </summary>
    private static bool IsAnonymousStaticAsset(HttpRequest request)
    {
        if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method))
        {
            return false;
        }

        string path = request.Path.HasValue ? request.Path.Value! : string.Empty;

        foreach (string exact in AnonymousExactPaths)
        {
            if (path.Equals(exact, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        foreach (string prefix in AnonymousPathPrefixes)
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return path.StartsWith("/favicon.", StringComparison.OrdinalIgnoreCase);
    }


    private static string? ExtractApiKey(HttpRequest request)
    {
        if (request.Headers.TryGetValue("Authorization", out StringValues authorization))
        {
            string? value = authorization.ToString();
            if (value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                return value["Bearer ".Length..].Trim();
            }
        }

        if (request.Query.TryGetValue("access_token", out StringValues queryToken))
        {
            string? token = queryToken.ToString();
            return string.IsNullOrWhiteSpace(token) ? null : token;
        }

        return null;
    }

    private bool TryResolveRole(string apiKey, out string? role)
    {
        role = null;

        string? adminKey = _configuration["Gateway:AdminApiKey"] ?? _configuration["InstantAIGate:AdminApiKey"];
        if (adminKey is not null && FixedTimeEquals(apiKey, adminKey))
        {
            role = AdminRole;
            return true;
        }

        string? tenantKey = _configuration["Gateway:TenantApiKey"] ?? _configuration["InstantAIGate:TenantApiKey"];
        if (tenantKey is not null && FixedTimeEquals(apiKey, tenantKey))
        {
            role = UserRole;
            return true;
        }

        return false;
    }

    /// <summary>Constant-time comparison to mitigate timing side-channels on key material.</summary>
    private static bool FixedTimeEquals(string left, string right)
        => CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(left),
            Encoding.UTF8.GetBytes(right));
}