using InstantAIGate.Application.Dtos.Requests;
using InstantAIGate.Application.Interfaces.Inference;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;

namespace InstantAIGate.Infrastructure.Inference.Adapters
{
    /// <summary>
    /// Router that inspects incoming chat requests and dynamically dispatches them 
    /// to either the text-only LlamaChatAdapter or the vision-enabled MultimodalChatAdapter.
    /// </summary>
    public class ChatAdapterRouter : IChatAdapter
    {
        private readonly LlamaChatAdapter _textAdapter;
        private readonly MultimodalChatAdapter _visionAdapter;
        private readonly ILogger<ChatAdapterRouter> _logger;

        public ChatAdapterRouter(
            LlamaChatAdapter textAdapter,
            MultimodalChatAdapter visionAdapter,
            ILogger<ChatAdapterRouter> logger)
        {
            _textAdapter = textAdapter;
            _visionAdapter = visionAdapter;
            _logger = logger;
        }

        public Task<string> GenerateAsync(LlamaChatRequest request, CancellationToken ct = default)
        {
            var adapter = SelectAdapter(request);
            return adapter.GenerateAsync(request, ct);
        }

        public IAsyncEnumerable<string> StreamAsync(LlamaChatRequest request, [EnumeratorCancellation] CancellationToken ct = default)
        {
            var adapter = SelectAdapter(request);
            return adapter.StreamAsync(request, ct);
        }

        /// <summary>
        /// Inspects the request messages for any image content parts.
        /// </summary>
        private IChatAdapter SelectAdapter(LlamaChatRequest request)
        {
            // Check if any message contains ContentParts of type "image_url"
            bool hasImages = request.Messages?.Any(m =>
                m.ContentParts?.Any(p => p.Type == "image_url") == true) == true;

            if (hasImages)
            {
                _logger.LogDebug("Multimodal content detected. Routing request to MultimodalChatAdapter for model '{Model}'.", request.Model);
                return _visionAdapter;
            }

            _logger.LogDebug("Text-only content detected. Routing request to LlamaChatAdapter for model '{Model}'.", request.Model);
            return _textAdapter;
        }
    }
}