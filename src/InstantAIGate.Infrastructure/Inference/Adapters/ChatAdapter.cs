using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using InstantAIGate.Application.Dtos.Requests;
using InstantAIGate.Application.Interfaces.Inference;
using InstantAIGate.Infrastructure.Inference.Facades;
using Microsoft.Extensions.Logging;

namespace InstantAIGate.Infrastructure.Inference.Adapters
{
    public class ChatAdapter : IChatAdapter
    {
        private readonly ILlamaEngineFacade _engineFacade;
        private readonly ModelManager _modelManager;
        private readonly ILogger<ChatAdapter> _logger;

        public ChatAdapter(
            ILlamaEngineFacade engineFacade,
            ModelManager modelManager,
            ILogger<ChatAdapter> logger)
        {
            _engineFacade = engineFacade ?? throw new ArgumentNullException(nameof(engineFacade));
            _modelManager = modelManager ?? throw new ArgumentNullException(nameof(modelManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<string> GenerateAsync(ChatRequest request, CancellationToken ct = default)
        {
            var sb = new StringBuilder();
            await foreach (var token in StreamAsync(request, ct))
            {
                sb.Append(token);
            }

            return sb.ToString();
        }

        public async IAsyncEnumerable<string> StreamAsync(
            ChatRequest request,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            var modelSettings = _modelManager.GetActiveSettings();
            if (modelSettings == null)
            {
                throw new InvalidOperationException("No active model is currently loaded in the system.");
            }

            string activeRepoId = modelSettings.RepoId;

            if (!string.Equals(request.Model, activeRepoId, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("Client requested '{RequestedModel}', dynamically routing to active model '{ActiveModel}'.", request.Model, activeRepoId);
            }

            var messages = request.Messages.ToList();

            int contextLimit = (int)modelSettings.ContextSize;
            int maxTokens = request.MaxTokens > 0 ? request.MaxTokens : 512;
            int availableTokensForPrompt = contextLimit - maxTokens;

            if (availableTokensForPrompt <= 0)
            {
                throw new InvalidOperationException($"Invalid context limit configuration. Limit: {contextLimit}, MaxTokens: {maxTokens}");
            }

            string prompt = BuildPromptFromMessages(messages);
            int tokenCount = await _engineFacade.GetTokenCountAsync(activeRepoId, prompt, ct);

            while (tokenCount > availableTokensForPrompt && messages.Count > 1)
            {
                int oldestMessageIndex = messages.FindIndex(m => !string.Equals(m.Role, "system", StringComparison.OrdinalIgnoreCase));

                if (oldestMessageIndex != -1)
                {
                    messages.RemoveAt(oldestMessageIndex);

                    if (oldestMessageIndex < messages.Count && string.Equals(messages[oldestMessageIndex].Role, "assistant", StringComparison.OrdinalIgnoreCase))
                    {
                        messages.RemoveAt(oldestMessageIndex);
                    }

                    _logger.LogDebug("Trimmed history. Current tokens: {Tokens}/{Limit}", tokenCount, availableTokensForPrompt);
                }
                else
                {
                    break;
                }

                prompt = BuildPromptFromMessages(messages);
                tokenCount = await _engineFacade.GetTokenCountAsync(activeRepoId, prompt, ct);
            }

            if (tokenCount > availableTokensForPrompt)
            {
                throw new InvalidOperationException("Prompt exceeds context size even after aggressively trimming history.");
            }

            var settings = new InferenceSettings
            {
                ModelId = activeRepoId,
                Temperature = request.Temperature,
                TopK = request.TopK,
                TopP = request.TopP,
                MaxTokens = maxTokens
            };

            var accumulatedText = new StringBuilder();
            var systemStopSequences = new[] { "<|im_end|>", "<|endoftext|>" };

            await foreach (var piece in _engineFacade.StreamGenerationAsync(prompt, settings, ct))
            {
                accumulatedText.Append(piece);
                string currentText = accumulatedText.ToString();
                bool shouldStop = false;

                foreach (var stopSeq in systemStopSequences)
                {
                    if (currentText.Contains(stopSeq))
                    {
                        shouldStop = true;
                        break;
                    }
                }

                if (!shouldStop && request.Stop != null && request.Stop.Count > 0)
                {
                    foreach (var stopSeq in request.Stop)
                    {
                        if (!string.IsNullOrEmpty(stopSeq) && currentText.Contains(stopSeq))
                        {
                            shouldStop = true;
                            break;
                        }
                    }
                }

                if (shouldStop)
                {
                    break;
                }

                yield return piece;
            }
        }

        private string BuildPromptFromMessages(List<ChatMessage> messages)
        {
            var sb = new StringBuilder();
            foreach (var msg in messages)
            {
                sb.AppendLine($"<|im_start|>{msg.Role}");
                sb.AppendLine(msg.Content);
                sb.AppendLine("<|im_end|>");
            }

            sb.Append("<|im_start|>assistant\n");

            return sb.ToString();
        }
    }
}