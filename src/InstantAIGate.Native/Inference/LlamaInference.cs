namespace InstantAIGate.Native.Inference;

using InstantAIGate.Core.Dtos.Config;
using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Interfaces.Inference;
using InstantAIGate.Native.Bindings;
using Microsoft.Extensions.Logging;
using System;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Core inference engine handling P/Invoke calls to llama.cpp.
/// Responsible for tokenization, prompt evaluation, and token generation.
/// </summary>
public class LlamaInference : IInferenceEngine, IDisposable
{
    private readonly IModelManager _modelManager;
    private readonly ILogger<LlamaInference> _logger;
    private bool _disposed;

    public LlamaInference(
        IModelManager modelManager,
        ILogger<LlamaInference> logger)
    {
        _modelManager = modelManager;
        _logger = logger;
    }

    public async Task<int[]> TokenizeDataAsync(string modelId, string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Prompt cannot be empty. Check your ChatML formatting.");
        }

        using var model = await _modelManager.AcquireModelAsync(modelId, ct);
        IntPtr vocab = LlamaNative.llama_model_get_vocab(model.Handle);

        byte[] textBytes = Encoding.UTF8.GetBytes(text);

        // Allocate buffer with overhead for special tokens
        int[] tokens = new int[textBytes.Length + 64];

        int count = LlamaNative.llama_tokenize(vocab, textBytes, textBytes.Length, tokens, tokens.Length, false, true);

        if (count < 0)
        {
            tokens = new int[-count];
            count = LlamaNative.llama_tokenize(vocab, textBytes, textBytes.Length, tokens, tokens.Length, false, true);
        }

        if (count <= 0)
        {
            throw new InvalidOperationException($"llama_tokenize failed. Returned token count: {count}");
        }

        var result = new int[count];
        Array.Copy(tokens, result, count);
        return result;
    }

    public async IAsyncEnumerable<string> StreamGenerationAsync(
        string modelId, string prompt, InferenceSettings settings,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var model = await _modelManager.AcquireModelAsync(modelId, ct);
        using var context = await _modelManager.AcquireContextAsync(modelId, ct);

        IntPtr vocab = LlamaNative.llama_model_get_vocab(model.Handle);
        IntPtr ctxHandle = context.TextContext.Handle;

        int[] tokens = await TokenizeDataAsync(modelId, prompt, ct);

        var chainParams = LlamaNative.llama_sampler_chain_default_params();
        IntPtr sampler = LlamaNative.llama_sampler_chain_init(chainParams);

        try
        {
            // Initialize core samplers. 
            // Note: Penalty samplers are intentionally omitted for Instruct models to prevent penalty collapse.
            LlamaNative.llama_sampler_chain_add(sampler, LlamaNative.llama_sampler_init_top_k(settings.TopK > 0 ? settings.TopK : 40));
            LlamaNative.llama_sampler_chain_add(sampler, LlamaNative.llama_sampler_init_top_p(settings.TopP > 0 ? settings.TopP : 0.9f, 1));
            LlamaNative.llama_sampler_chain_add(sampler, LlamaNative.llama_sampler_init_temp(settings.Temperature > 0 ? settings.Temperature : 0.7f));

            uint activeSeed = settings.Seed ?? (uint)Random.Shared.Next();
            LlamaNative.llama_sampler_chain_add(sampler, LlamaNative.llama_sampler_init_dist(activeSeed));

            int lastEvalBatchSize = 0;

            // 1. Evaluate prompt in batches
            unsafe
            {
                int maxBatchSize = settings.BatchSize > 0 ? settings.BatchSize : 512;
                var batch = LlamaNative.llama_batch_init(maxBatchSize, 0, 1);

                try
                {
                    int* tokenPtr = (int*)batch.Token;
                    int* posPtr = (int*)batch.Pos;
                    int* nSeqIdPtr = (int*)batch.NSeqId;
                    int** seqIdPtr = (int**)batch.SeqId;
                    byte* logitsPtr = (byte*)batch.Logits;

                    for (int i = 0; i < tokens.Length; i += maxBatchSize)
                    {
                        ct.ThrowIfCancellationRequested();

                        int evalBatchSize = Math.Min(tokens.Length - i, maxBatchSize);
                        lastEvalBatchSize = evalBatchSize;

                        for (int j = 0; j < evalBatchSize; j++)
                        {
                            tokenPtr[j] = tokens[i + j];
                            posPtr[j] = i + j;
                            nSeqIdPtr[j] = 1;
                            seqIdPtr[j][0] = 0;
                            // Only extract logits for the very last token of the prompt
                            logitsPtr[j] = (byte)((i + j == tokens.Length - 1) ? 1 : 0);
                        }

                        batch.NTokens = evalBatchSize;

                        int evalResult = LlamaNative.llama_decode(ctxHandle, batch);
                        if (evalResult != 0)
                        {
                            throw new InvalidOperationException($"Prompt evaluation failed with code: {evalResult}");
                        }
                    }
                }
                finally
                {
                    LlamaNative.llama_batch_free(batch);
                }
            }

            // 2. Generation loop
            int eos = LlamaNative.llama_vocab_eos(vocab);
            int generated = 0;
            int currentPos = tokens.Length;

            var utf8Decoder = Encoding.UTF8.GetDecoder();
            byte[] pieceBuffer = new byte[256];
            char[] charBuffer = new char[512];

            while (generated < settings.MaxTokens)
            {
                ct.ThrowIfCancellationRequested();

                // Correct logit index to avoid memory access violations
                int logitIndex = (generated == 0) ? (lastEvalBatchSize - 1) : 0;
                int token = LlamaNative.llama_sampler_sample(sampler, ctxHandle, logitIndex);

                // Hardware-level stop condition (includes Qwen specific tokens)
                if (token == eos || token < 0 || token == 151645 || token == 151643)
                {
                    break;
                }

                LlamaNative.llama_sampler_accept(sampler, token);

                int pieceSize = LlamaNative.llama_token_to_piece(vocab, token, pieceBuffer, pieceBuffer.Length, 0, true);

                if (pieceSize < 0)
                {
                    pieceBuffer = new byte[-pieceSize];
                    pieceSize = LlamaNative.llama_token_to_piece(vocab, token, pieceBuffer, pieceBuffer.Length, 0, true);
                }

                if (pieceSize > 0)
                {
                    int charsDecoded = utf8Decoder.GetChars(pieceBuffer, 0, pieceSize, charBuffer, 0, false);
                    if (charsDecoded > 0)
                    {
                        yield return new string(charBuffer, 0, charsDecoded);
                    }
                }

                generated++;

                unsafe
                {
                    var singleBatch = LlamaNative.llama_batch_init(1, 0, 1);
                    try
                    {
                        ((int*)singleBatch.Token)[0] = token;
                        ((int*)singleBatch.Pos)[0] = currentPos++;
                        ((int*)singleBatch.NSeqId)[0] = 1;
                        ((int**)singleBatch.SeqId)[0][0] = 0;
                        ((byte*)singleBatch.Logits)[0] = 1;
                        singleBatch.NTokens = 1;

                        int stepResult = LlamaNative.llama_decode(ctxHandle, singleBatch);
                        if (stepResult != 0)
                        {
                            break;
                        }
                    }
                    finally
                    {
                        LlamaNative.llama_batch_free(singleBatch);
                    }
                }
            }

            // Flush remaining characters
            int finalChars = utf8Decoder.GetChars(pieceBuffer, 0, 0, charBuffer, 0, true);
            if (finalChars > 0)
            {
                yield return new string(charBuffer, 0, finalChars);
            }
        }
        finally
        {
            LlamaNative.llama_sampler_free(sampler);
        }
    }


    public async Task<string> ApplyChatTemplateAsync(string modelId, IEnumerable<ChatMessage> messages, CancellationToken ct = default)
    {
        var msgList = messages.ToList();
        var nativeMessages = new LlamaNative.llama_chat_message[msgList.Count];


        for (int i = 0; i < msgList.Count; i++)
        {
            nativeMessages[i] = new LlamaNative.llama_chat_message
            {
                role = msgList[i].Role,
                content = msgList[i].Content
            };
        }

        int requiredSize = LlamaNative.llama_chat_apply_template(
            null, nativeMessages, (nuint)nativeMessages.Length, true, null, 0);

        if (requiredSize < 0)
        {
            throw new InvalidOperationException("Failed to apply chat template. Metadata might be missing or invalid.");
        }


        byte[] buffer = new byte[requiredSize + 1];
        int finalSize = LlamaNative.llama_chat_apply_template(
            null, nativeMessages, (nuint)nativeMessages.Length, true, buffer, buffer.Length);

        return Encoding.UTF8.GetString(buffer, 0, finalSize);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }
}