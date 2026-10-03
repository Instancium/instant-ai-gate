namespace InstantAIGate.Server.Middleware;

using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

public class ApiKeyAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string _adminApiKey;

    public ApiKeyAuthMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;
        _adminApiKey = configuration["InstantAIGate:AdminApiKey"] ?? string.Empty;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        string path = context.Request.Path.Value ?? string.Empty;
        if (path.StartsWith("/health", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        string? token = null;

        if (context.Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            string headerValue = authHeader.ToString().Trim();
            if (headerValue.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                token = headerValue.Substring("Bearer ".Length).Trim();
            }
            else if (!string.IsNullOrWhiteSpace(headerValue))
            {
                token = headerValue;
            }
        }

        if (string.IsNullOrWhiteSpace(token) && context.Request.Query.TryGetValue("access_token", out var queryToken))
        {
            token = queryToken.ToString().Trim();
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        List<Claim> claims;
        if (!string.IsNullOrEmpty(_adminApiKey) && string.Equals(token, _adminApiKey, StringComparison.Ordinal))
        {
            claims =
            [
                new Claim(ClaimTypes.Role, "Admin"),
                new Claim(ClaimTypes.Role, "User"),
                new Claim("TenantId", "admin")
            ];
        }
        else
        {
            claims =
            [
                new Claim(ClaimTypes.Role, "User"),
                new Claim("TenantId", token)
            ];
        }

        var identity = new ClaimsIdentity(claims, "ApiKey");
        context.User = new ClaimsPrincipal(identity);

        await _next(context);
    }
}