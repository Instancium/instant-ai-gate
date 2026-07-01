using InstantAIGate.Admin.Config;
using InstantAIGate.Application.Config;
using Microsoft.Extensions.Options;

namespace InstantAIGate.Admin
{
    public class ApiKeyHandler : DelegatingHandler
    {
        private readonly GatewayConfig _gatewayConfig;

        public ApiKeyHandler(GatewayConfig gatewayConfig)
        {
            _gatewayConfig = gatewayConfig;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (!string.IsNullOrWhiteSpace(_gatewayConfig.AdminKey) &&
                !string.Equals(_gatewayConfig.AdminKey, "skip", StringComparison.OrdinalIgnoreCase))
            {
                request.Headers.Remove("X-API-Key");
                request.Headers.Add("X-API-Key", _gatewayConfig.AdminKey);
            }

            return base.SendAsync(request, cancellationToken);
        }
    }
}
