namespace InstantAIGate.Native.Inference;

using InstantAIGate.Core.Dtos.Config;
using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Exceptions;
using InstantAIGate.Core.Interfaces.Inference;
using InstantAIGate.Core.Interfaces.Infrastructure;
using InstantAIGate.Core.Services.Inference;
using InstantAIGate.Native.Bindings;
using InstantAIGate.Native.Handles;
using Microsoft.Extensions.Logging;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

public class LlamaInference : IInferenceEngine, IDisposable
{
    private readonly IModelManager _modelManager;
    private readonly ILogger<LlamaInference> _logger;
    private readonly IMediaResolver _mediaResolver;
    private readonly ISessionInferenceManager _sessionManager;
    private readonly IBackendFacade _backendFacade;
    private const int EvalTpsRefreshTokenInterval = 5;
    private bool _disposed;

    public LlamaInference(
        IModelManager modelManager,
        ILogger<LlamaInference> logger,
        IBackendFacade backendFacade,
        IMediaResolver mediaResolver,
        ISessionInferenceManager sessionManager)
    {
        _modelManager = modelManager;
        _logger = logger;
        _mediaResolver = mediaResolver;
        _sessionManager = sessionManager;
        _backendFacade = backendFacade;
    }

    public async Task<int> GetEmbeddingDimensionAsync(string modelId, CancellationToken ct = default)
    {
        using var model = await _modelManager.AcquireModelAsync(modelId, ct);
        return _backendFacade.GetEmbeddingDimension(model.Handle);
    }

    private static IntPtr UnwrapModel(ModelWeights weights) =>
        weights.Handle is LlamaModelHandle mh ? mh.Pointer : throw new InvalidCastException("Invalid model handle.");
    private static IntPtr UnwrapContext(ModelContext ctx) =>
        ctx.Handle is LlamaContextHandle ch ? ch.Pointer : throw new InvalidCastException("Invalid context handle.");

    public async Task<int[]> TokenizeDataAsync(string modelId, string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Prompt cannot be empty. Check your ChatML formatting.");
        }
        using var model = await _modelManager.AcquireModelAsync(modelId, ct);
        IntPtr nativeModelPtr = UnwrapModel(model);
        IntPtr vocab = LlamaNative.llama_model_get_vocab(nativeModelPtr);
        bool addBos = LlamaNative.llama_vocab_get_add_bos(vocab);
        byte[] textBytes = Encoding.UTF8.GetBytes(text);
        int[] tokens = new int[textBytes.Length + 64];
        int count = LlamaNative.llama_tokenize(vocab, textBytes, textBytes.Length, tokens, tokens.Length, addBos, true);
        if (count < 0)
        {
            tokens = new int[-count];
            count = LlamaNative.llama_tokenize(vocab, textBytes, textBytes.Length, tokens, tokens.Length, addBos, true);
        }
        if (count <= 0)
        {
            throw new InvalidOperationException($"Failed to tokenize string: '{text}'");
        }
        Array.Resize(ref tokens, count);
        return tokens;
    }

    public async Task<string> ApplyChatTemplateAsync(string modelId, IEnumerable<ChatMessage> messages, CancellationToken ct = default)
    {
        using var model = await _modelManager.AcquireModelAsync(modelId, ct);
        IntPtr nativeModelPtr = UnwrapModel(model);
        IntPtr tmplPtr = LlamaNative.llama_model_chat_template(nativeModelPtr, null);
        string tmpl = tmplPtr != IntPtr.Zero ? Marshal.PtrToStringUTF8(tmplPtr)! : "chatml";
        var msgList = messages.ToList();
        var nativeMessages = new LlamaNative.llama_chat_message[msgList.Count];
        for (int i = 0; i < msgList.Count; i++)
        {
            var sb = new StringBuilder();
            var parts = msgList[i].Parts ?? new List<MessageContent> { new TextContent(msgList[i].Content) };
            foreach (var part in parts)
            {
                if (part is not TextContent) sb.Append("<__media__>\n");
            }
            foreach (var part in parts)
            {
                if (part is TextContent textPart) sb.Append(textPart.Text);
            }
            nativeMessages[i] = new LlamaNative.llama_chat_message
            {
                role = msgList[i].Role,
                content = sb.ToString()
            };
        }
        int requiredSize = LlamaNative.llama_chat_apply_template(
            tmpl, nativeMessages, (nuint)nativeMessages.Length, true, null, 0);
        if (requiredSize < 0)
        {
            throw new InvalidOperationException("Failed to apply chat template.");
        }
        byte[] buffer = new byte[requiredSize + 1];
        int finalSize = LlamaNative.llama_chat_apply_template(
            tmpl, nativeMessages, (nuint)nativeMessages.Length, true, buffer, buffer.Length);
        return Encoding.UTF8.GetString(buffer, 0, finalSize);
    }

    public async Task<EmbeddingResponse> GenerateEmbeddingsAsync(
        string modelId,
        EmbeddingRequest request,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
        {
            throw new ArgumentException("Input text cannot be empty for embedding generation.", nameof(request));
        }

        using var model = await _modelManager.AcquireModelAsync(modelId, ct);
        IntPtr nativeModelPtr = UnwrapModel(model);

        var activeSettings = _modelManager.GetModelSettings(modelId) ?? _modelManager.GetActiveSettings(modelId);
        if (activeSettings == null || !activeSettings.Embeddings)
        {
            throw new InvalidOperationException(
                $"Model '{modelId}' is not configured for embeddings. " +
                "Ensure ModelSettings.Embeddings is true in hardware profile.");
        }

        using var context = await _modelManager.AcquireContextAsync(modelId, ct);
        IntPtr ctxHandle = UnwrapContext(context.TextContext);
        IntPtr vocab = LlamaNative.llama_model_get_vocab(nativeModelPtr);

        byte[] textBytes = Encoding.UTF8.GetBytes(request.Text);
        int[] tokens = new int[textBytes.Length + 64];
        bool addBos = LlamaNative.llama_vocab_get_add_bos(vocab);
        int count = LlamaNative.llama_tokenize(vocab, textBytes, textBytes.Length, tokens, tokens.Length, addBos, true);

        if (count < 0)
        {
            tokens = new int[-count];
            count = LlamaNative.llama_tokenize(vocab, textBytes, textBytes.Length, tokens, tokens.Length, addBos, true);
        }

        if (count <= 0)
        {
            throw new InvalidOperationException($"Failed to tokenize input text for embedding: '{request.Text}'");
        }

        Array.Resize(ref tokens, count);

        if (request.MaxTokens.HasValue && tokens.Length > request.MaxTokens.Value)
        {
            Array.Resize(ref tokens, request.MaxTokens.Value);
        }

        uint maxContextLimit = activeSettings.ContextSize > 0
            ? (uint)activeSettings.ContextSize
            : LlamaNative.llama_n_ctx(ctxHandle);

        if (tokens.Length > maxContextLimit)
        {
            throw new ContextOverflowException(
                "embedding",
                0,
                tokens.Length,
                0,
                (int)maxContextLimit);
        }

        int maxBatchSize = activeSettings.BatchSize > 0 ? activeSettings.BatchSize : 512;

        unsafe
        {
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

                    for (int j = 0; j < evalBatchSize; j++)
                    {
                        tokenPtr[j] = tokens[i + j];
                        posPtr[j] = i + j;
                        nSeqIdPtr[j] = 1;
                        seqIdPtr[j][0] = 0;
                        logitsPtr[j] = 1;
                    }

                    batch.NTokens = evalBatchSize;
                    int decodeRes = LlamaNative.llama_decode(ctxHandle, batch);
                    if (decodeRes != 0)
                    {
                        throw new InvalidOperationException($"llama_decode failed for embedding generation with code: {decodeRes}");
                    }
                }
            }
            finally
            {
                LlamaNative.llama_batch_free(batch);
            }
        }

        float[]? rawEmbeddings = _backendFacade.GetEmbeddings(context.TextContext.Handle, 0);
        if (rawEmbeddings == null || rawEmbeddings.Length == 0)
        {
            throw new InvalidOperationException("Failed to extract embeddings from context. Model may not support embeddings.");
        }

        if (request.Normalize)
        {
            double norm = 0;
            for (int i = 0; i < rawEmbeddings.Length; i++)
            {
                norm += rawEmbeddings[i] * rawEmbeddings[i];
            }
            norm = Math.Sqrt(norm);

            if (norm > 0)
            {
                for (int i = 0; i < rawEmbeddings.Length; i++)
                {
                    rawEmbeddings[i] = (float)(rawEmbeddings[i] / norm);
                }
            }
        }

        return new EmbeddingResponse(
            Embedding: rawEmbeddings,
            TokenCount: tokens.Length,
            EmbeddingDimension: rawEmbeddings.Length
        );
    }

    public async IAsyncEnumerable<string> StreamDeltaGenerationAsync(
        string sessionId,
        ChatMessage deltaMessage,
        InferenceSettings? overrideSettings = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_sessionManager.TryGetSessionRepoId(sessionId, out var repoId) || string.IsNullOrEmpty(repoId))
        {
            throw new InvalidOperationException($"Active session '{sessionId}' was not found.");
        }

        using var sessionGate = await _sessionManager.AcquireSessionExecutionGateAsync(sessionId, ct);
        var inferenceCtx = await _sessionManager.GetOrCreateContextAsync(sessionId, ct);
        using var model = await _modelManager.AcquireModelAsync(repoId, ct);

        IntPtr ctxHandle = UnwrapContext(inferenceCtx.TextContext);
        IntPtr modelHandle = UnwrapModel(model);
        IntPtr vocab = LlamaNative.llama_model_get_vocab(modelHandle);
        IntPtr mtmdCtxHandle = inferenceCtx.VisionContext?.Handle is MtmdVisionHandle vh ? vh.Pointer : IntPtr.Zero;

        var pastTokens = _sessionManager.GetPastTokensCount(sessionId);

        string formattedDelta = await ApplyChatTemplateAsync(repoId, new[] { deltaMessage }, ct);
        int[] deltaTokens = await TokenizeDataAsync(repoId, formattedDelta, ct);

        if (pastTokens > 0 && deltaTokens.Length > 0)
        {
            int bosTokenId = LlamaNative.llama_vocab_bos(vocab);
            if (deltaTokens[0] == bosTokenId)
            {
                var slicedTokens = new int[deltaTokens.Length - 1];
                Array.Copy(deltaTokens, 1, slicedTokens, 0, slicedTokens.Length);
                deltaTokens = slicedTokens;
            }
        }

        var activeConfig = _modelManager.GetModelSettings(repoId) ?? _modelManager.GetActiveSettings(repoId);
        uint maxContextLimit = activeConfig != null ? (uint)activeConfig.ContextSize : LlamaNative.llama_n_ctx(ctxHandle);
        int maxBatchSize = activeConfig?.BatchSize > 0 ? activeConfig.BatchSize : 512;

        var mediaParts = deltaMessage.Parts?.Where(p => p is not TextContent).ToList() ?? new List<MessageContent>();
        using var mediaContext = mediaParts.Count > 0
            ? await _mediaResolver.ResolveMediaAsync(mediaParts, ct)
            : null;
        var localImagePaths = mediaContext?.LocalFilePaths;

        int estimatedTokens = deltaTokens.Length + ((localImagePaths?.Count ?? 0) * 1024);
        int requiredReserve = overrideSettings?.MaxTokens > 0
            ? Math.Min(overrideSettings.MaxTokens, (int)(maxContextLimit * 0.5))
            : 256;

        if (pastTokens + estimatedTokens + requiredReserve > maxContextLimit)
        {
            throw new ContextOverflowException(
                sessionId,
                pastTokens,
                estimatedTokens,
                requiredReserve,
                (int)maxContextLimit);
        }

        _sessionManager.AppendSessionTokens(sessionId, deltaTokens);

        _backendFacade.ResetContextPerformance(inferenceCtx.TextContext.Handle);
        var promptStopwatch = Stopwatch.StartNew();
        int currentPos = pastTokens;

        if (mtmdCtxHandle != IntPtr.Zero && localImagePaths != null && localImagePaths.Count > 0)
        {
            var bitmapHandles = new List<MtmdNative.MtmdBitmapHandle>();
            IntPtr[] bitmapPtrs = new IntPtr[localImagePaths.Count];
            try
            {
                var opt = MtmdNative.mtmd_helper_init_opt_default();
                for (int i = 0; i < localImagePaths.Count; i++)
                {
                    var bmpWrapper = MtmdNative.mtmd_helper_bitmap_init_from_file(
                        mtmdCtxHandle, localImagePaths[i], false, opt);
                    if (bmpWrapper.Bitmap == IntPtr.Zero)
                        throw new InvalidOperationException("Failed to load image.");
                    var handle = new MtmdNative.MtmdBitmapHandle(bmpWrapper.Bitmap);
                    bitmapHandles.Add(handle);
                    bitmapPtrs[i] = handle.DangerousGetHandle();
                }

                IntPtr rawHandle = MtmdNative.mtmd_input_chunks_init();
                using var chunksHandle = new MtmdNative.MtmdInputChunksHandle(rawHandle);
                IntPtr textPtr = Marshal.StringToCoTaskMemUTF8(formattedDelta);
                try
                {
                    var inputText = new MtmdInputText
                    {
                        Text = textPtr,
                        TextLen = (UIntPtr)Encoding.UTF8.GetByteCount(formattedDelta),
                        AddSpecial = true,
                        ParseSpecial = true
                    };
                    int tokResult = MtmdNative.mtmd_tokenize(
                        mtmdCtxHandle, chunksHandle, ref inputText, bitmapPtrs, (UIntPtr)bitmapPtrs.Length);
                    if (tokResult != 0)
                        throw new InvalidOperationException($"mtmd_tokenize failed with code: {tokResult}");

                    int evalResult = MtmdNative.mtmd_helper_eval_chunks(
                        mtmdCtxHandle, ctxHandle, chunksHandle, pastTokens, 0, maxBatchSize, true, out currentPos);
                    if (evalResult != 0)
                        throw new InvalidOperationException($"mtmd_helper_eval_chunks failed: {evalResult}");
                }
                finally
                {
                    Marshal.FreeCoTaskMem(textPtr);
                }
            }
            finally
            {
                foreach (var handle in bitmapHandles) handle.Dispose();
            }
        }
        else
        {
            unsafe
            {
                var batch = LlamaNative.llama_batch_init(maxBatchSize, 0, 1);
                try
                {
                    int* tokenPtr = (int*)batch.Token;
                    int* posPtr = (int*)batch.Pos;
                    int* nSeqIdPtr = (int*)batch.NSeqId;
                    int** seqIdPtr = (int**)batch.SeqId;
                    byte* logitsPtr = (byte*)batch.Logits;

                    for (int i = 0; i < deltaTokens.Length; i += maxBatchSize)
                    {
                        ct.ThrowIfCancellationRequested();
                        int evalBatchSize = Math.Min(deltaTokens.Length - i, maxBatchSize);
                        for (int j = 0; j < evalBatchSize; j++)
                        {
                            tokenPtr[j] = deltaTokens[i + j];
                            posPtr[j] = pastTokens + i + j;
                            nSeqIdPtr[j] = 1;
                            seqIdPtr[j][0] = 0;
                            logitsPtr[j] = (byte)((i + j == deltaTokens.Length - 1) ? 1 : 0);
                        }
                        batch.NTokens = evalBatchSize;
                        int decodeRes = LlamaNative.llama_decode(ctxHandle, batch);
                        if (decodeRes != 0)
                            throw new InvalidOperationException(
                                $"llama_decode failed on prompt ingestion with code: {decodeRes}");
                    }
                    currentPos = pastTokens + deltaTokens.Length;
                }
                finally
                {
                    LlamaNative.llama_batch_free(batch);
                }
            }
        }

        promptStopwatch.Stop();
        _sessionManager.UpdatePastTokensCount(sessionId, currentPos);
        

        var promptPerf = _backendFacade.GetContextPerformance(inferenceCtx.TextContext.Handle);
        double promptTps = PerformanceMetricsCalculator.ComputePromptTps(promptPerf);
        if (promptTps <= 0.0 && deltaTokens.Length > 0)
        {
            double elapsedPromptMs = Math.Max(0.1, promptStopwatch.Elapsed.TotalMilliseconds);
            promptTps = PerformanceMetricsCalculator.ComputeTps(deltaTokens.Length, elapsedPromptMs);
        }
        _sessionManager.UpdateSessionPerformance(sessionId, promptTps, evalTps: 0.0);

        var samplerParams = LlamaNative.llama_sampler_chain_default_params();
        IntPtr samplerChain = LlamaNative.llama_sampler_chain_init(samplerParams);
        try
        {
            // Sampler chain order follows llama.cpp guidance:
            // penalties -> top_k -> top_p -> temp -> dist.
            // Repetition penalty is CRITICAL: without it Qwen-family models
            // collapse into degenerate n-gram loops on list/bullet formats.
            LlamaNative.llama_sampler_chain_add(
                samplerChain,
                LlamaNative.llama_sampler_init_top_k(overrideSettings?.TopK ?? 40));

            LlamaNative.llama_sampler_chain_add(
                samplerChain,
                LlamaNative.llama_sampler_init_top_p(overrideSettings?.TopP ?? 0.9f, 1));

            LlamaNative.llama_sampler_chain_add(
                samplerChain,
                LlamaNative.llama_sampler_init_temp(overrideSettings?.Temperature ?? 0.7f));

            LlamaNative.llama_sampler_chain_add(
                samplerChain,
                LlamaNative.llama_sampler_init_dist(
                    overrideSettings?.Seed ?? (uint)Random.Shared.Next()));

            int headroom = (int)maxContextLimit - currentPos;
            if (headroom <= 0)
            {
                throw new ContextOverflowException(sessionId, currentPos, 0, 1, (int)maxContextLimit);
            }

            int maxTokensToGenerate = overrideSettings?.MaxTokens is > 0
                ? (int)Math.Min(overrideSettings.MaxTokens, headroom)
                : headroom;

            int generatedCount = 0;
            var generatedTokens = new List<int>();

            // BPE-safe UTF-8 streaming via extracted decoder (see BpeStreamDecoder).
            var utf8Decoder = new BpeStreamDecoder();
            byte[] tokenPieceBuffer = new byte[64];
            var pendingChunks = new Queue<string>();

            // Degenerate-loop circuit breaker. Stops runaway repetition
            // instead of burning the whole context headroom.
            var repetitionGuard = new RepetitionGuard();
            bool loopDetected = false;

            var evalStopwatch = System.Diagnostics.Stopwatch.StartNew();
            var singleTokenBatch = LlamaNative.llama_batch_init(1, 0, 1);
            try
            {
                while (generatedCount < maxTokensToGenerate)
                {
                    ct.ThrowIfCancellationRequested();

                    int newTokenId = LlamaNative.llama_sampler_sample(samplerChain, ctxHandle, -1);
                    LlamaNative.llama_sampler_accept(samplerChain, newTokenId);

                    if (LlamaNative.llama_vocab_is_eog(vocab, newTokenId))
                        break;

                    generatedTokens.Add(newTokenId);
                    generatedCount++;

                    if (repetitionGuard.Feed(newTokenId))
                    {
                        loopDetected = true;
                        _logger.LogWarning(
                            "Degenerate repetition loop detected in session '{SessionId}' after {Count} tokens. Aborting generation.",
                            sessionId, generatedCount);
                        break;
                    }

                    int nPieces = LlamaNative.llama_token_to_piece(
                        vocab, newTokenId, tokenPieceBuffer, tokenPieceBuffer.Length,
                        lstrip: 0, special: false);
                    if (nPieces < 0)
                    {
                        tokenPieceBuffer = new byte[-nPieces];
                        nPieces = LlamaNative.llama_token_to_piece(
                            vocab, newTokenId, tokenPieceBuffer, tokenPieceBuffer.Length,
                            lstrip: 0, special: false);
                        if (nPieces < 0)
                            continue;
                    }
                    if (nPieces == 0)
                        continue;

                    utf8Decoder.Feed(tokenPieceBuffer, nPieces, chunk => pendingChunks.Enqueue(chunk));

                    while (pendingChunks.TryDequeue(out var readyChunk))
                    {
                        yield return readyChunk;
                    }

                    unsafe
                    {
                        ((int*)singleTokenBatch.Token)[0] = newTokenId;
                        ((int*)singleTokenBatch.Pos)[0] = currentPos++;
                        ((int*)singleTokenBatch.NSeqId)[0] = 1;
                        ((int**)singleTokenBatch.SeqId)[0][0] = 0;
                        ((byte*)singleTokenBatch.Logits)[0] = 1;
                        singleTokenBatch.NTokens = 1;

                        int decodeRes = LlamaNative.llama_decode(ctxHandle, singleTokenBatch);
                        if (decodeRes != 0)
                            throw new InvalidOperationException($"llama_decode failed: {decodeRes}");
                    }

                    if (generatedCount % EvalTpsRefreshTokenInterval == 0)
                    {
                        var genPerf = _backendFacade.GetContextPerformance(inferenceCtx.TextContext.Handle);
                        double liveEvalTps = PerformanceMetricsCalculator.ComputeEvalTps(genPerf);
                        if (liveEvalTps <= 0.0 && generatedCount > 0)
                        {
                            liveEvalTps = PerformanceMetricsCalculator.ComputeTps(
                                generatedCount,
                                Math.Max(0.1, evalStopwatch.Elapsed.TotalMilliseconds));
                        }
                        _sessionManager.UpdateSessionPerformance(sessionId, promptTps, liveEvalTps);
                    }
                }
            }
            finally
            {
                LlamaNative.llama_batch_free(singleTokenBatch);
            }

            // Final performance snapshot reflecting the completed generation.
            var finalPerf = _backendFacade.GetContextPerformance(inferenceCtx.TextContext.Handle);
            double finalPromptTps = PerformanceMetricsCalculator.ComputePromptTps(finalPerf);
            if (finalPromptTps <= 0.0)
            {
                finalPromptTps = promptTps;
            }

            double finalEvalTps = PerformanceMetricsCalculator.ComputeEvalTps(finalPerf);
            if (finalEvalTps <= 0.0 && generatedCount > 0)
            {
                finalEvalTps = PerformanceMetricsCalculator.ComputeTps(
                    generatedCount,
                    Math.Max(0.1, evalStopwatch.Elapsed.TotalMilliseconds));
            }
            _sessionManager.UpdateSessionPerformance(sessionId, finalPromptTps, finalEvalTps);

            utf8Decoder.Flush(chunk => pendingChunks.Enqueue(chunk));
            while (pendingChunks.TryDequeue(out var finalChunk))
            {
                yield return finalChunk;
            }

            if (loopDetected)
            {
                yield return "\n[generation stopped: repetitive loop detected]";
            }

            if (utf8Decoder.PendingByteCount > 0)
            {
                _logger.LogWarning(
                    "Stream ended with {Count} undecoded UTF-8 byte(s) in session '{SessionId}'.",
                    utf8Decoder.PendingByteCount, sessionId);
            }

            if (generatedCount >= maxTokensToGenerate && maxTokensToGenerate < headroom)
            {
                _logger.LogInformation(
                    "Generation for session '{SessionId}' hit MaxTokens={Limit} (finish_reason=length).",
                    sessionId, maxTokensToGenerate);
            }

            _sessionManager.UpdatePastTokensCount(sessionId, currentPos);
            if (generatedTokens.Count > 0)
            {
                _sessionManager.AppendSessionTokens(sessionId, generatedTokens.ToArray());
            }
        }
        finally
        {
            LlamaNative.llama_sampler_free(samplerChain);
        }
    }

    public void Dispose()
    {
        _disposed = true;
    }
}