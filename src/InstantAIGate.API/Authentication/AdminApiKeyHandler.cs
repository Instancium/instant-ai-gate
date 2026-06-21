using InstantAIGate.API.Config;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace InstantAIGate.API.Authentication
{
    /// <summary>
    /// Authorization requirement for admin API key policy.
    /// </summary>
    public class AdminApiKeyRequirement : IAuthorizationRequirement { }

    /// <summary>
    /// Custom authentication handler for validating API keys via X-Api-Key header or query string.
    /// </summary>
    public class AdminApiKeyHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        private const string ApiKeyHeaderName = "X-Api-Key";
        private readonly ApiKeyOptions _options;

        /// <summary>
        /// Initializes a new instance of the <see cref="AdminApiKeyHandler"/> class.
        /// </summary>
        public AdminApiKeyHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> schemeOptions,
            ILoggerFactory logger,
            UrlEncoder encoder,
            IOptions<ApiKeyOptions> apiOptions)
            : base(schemeOptions, logger, encoder)
        {
            _options = apiOptions.Value;
        }

        /// <summary>
        /// Handles authentication by validating the API key from request headers or query string.
        /// </summary>
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            // If AdminKey is empty or "skip", bypass authentication (for development)
            if (string.IsNullOrWhiteSpace(_options.AdminKey) ||
                string.Equals(_options.AdminKey, "skip", StringComparison.OrdinalIgnoreCase))
            {
                var skipClaims = new[] { new Claim(ClaimTypes.Name, "SkipAuth") };
                var skipIdentity = new ClaimsIdentity(skipClaims, Scheme.Name);
                var skipPrincipal = new ClaimsPrincipal(skipIdentity);
                var skipTicket = new AuthenticationTicket(skipPrincipal, Scheme.Name);

                return Task.FromResult(AuthenticateResult.Success(skipTicket));
            }

            string? extractedApiKey = null;

            // 1. Try to get the API key from the headers
            if (Request.Headers.TryGetValue(ApiKeyHeaderName, out var headerApiKey))
            {
                extractedApiKey = headerApiKey.ToString();
            }
            // 2. FALLBACK: Try to get the API key from the query string (Required for EventSource/SSE)
            else if (Request.Query.TryGetValue("apiKey", out var queryApiKey))
            {
                extractedApiKey = queryApiKey.ToString();
            }

            // If no key was found in either location
            if (string.IsNullOrWhiteSpace(extractedApiKey))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            // Validate the key
            if (!string.Equals(extractedApiKey, _options.AdminKey, StringComparison.Ordinal))
            {
                return Task.FromResult(AuthenticateResult.Fail("Invalid API Key"));
            }

            // Successful authentication
            var claims = new[]
            {
                new Claim(ClaimTypes.Name, "Admin"),
                new Claim(ClaimTypes.Role, "Admin")
            };
            var identity = new ClaimsIdentity(claims, Scheme.Name);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, Scheme.Name);

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }

        /// <summary>
        /// Handles 401 Unauthorized challenge with JSON response.
        /// </summary>
        protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            Response.StatusCode = 401;
            Response.ContentType = "application/json";

            var result = new
            {
                error = "Unauthorized",
                message = $"Invalid or missing API Key. Provide '{ApiKeyHeaderName}' header or '?apiKey=' query parameter."
            };

            await Response.WriteAsync(JsonSerializer.Serialize(result));
        }

        /// <summary>
        /// Handles 403 Forbidden response with JSON.
        /// </summary>
        protected override async Task HandleForbiddenAsync(AuthenticationProperties properties)
        {
            Response.StatusCode = 403;
            Response.ContentType = "application/json";

            var result = new
            {
                error = "Forbidden",
                message = "You don't have permission to access this resource."
            };

            await Response.WriteAsync(JsonSerializer.Serialize(result));
        }
    }
}