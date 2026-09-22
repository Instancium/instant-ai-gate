// File: src/InstantAIGate.Native/Inference/LlamaInference.cs
namespace InstantAIGate.Native.Inference;

using InstantAIGate.Core.Dtos.Config;
using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Interfaces.Inference;
using InstantAIGate.Core.Interfaces.Infrastructure;
using InstantAIGate.Native.Bindings;
using InstantAIGate.Native.Handles; 
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
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
    private bool _disposed;

    public LlamaInference(
        IModelManager modelManager,
        ILogger<LlamaInference> logger,
        IMediaResolver mediaResolver)
    {
        _modelManager = modelManager;
        _logger = logger;
        _mediaResolver = mediaResolver;
    }

    // HELPER: Safely unbox opaque handles to native pointers
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

        // Unboxing the interface to get the IntPtr
        IntPtr nativeModelPtr = UnwrapModel(model);
        IntPtr vocab = LlamaNative.llama_model_get_vocab(nativeModelPtr);

        byte[] textBytes = Encoding.UTF8.GetBytes(text);
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

    public async Task<string> ApplyChatTemplateAsync(
        string modelId,
        IEnumerable<ChatMessage> messages,
        CancellationToken ct = default)
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
                if (part is not TextContent)
                {
                    sb.Append("<__media__>\n");
                }
            }

    
            foreach (var part in parts)
            {
                if (part is TextContent textPart)
                {
                    sb.Append(textPart.Text);
                }
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
            throw new InvalidOperationException("Failed to apply chat template.");

        byte[] buffer = new byte[requiredSize + 1];
        int finalSize = LlamaNative.llama_chat_apply_template(
            tmpl, nativeMessages, (nuint)nativeMessages.Length, true, buffer, buffer.Length);

        return Encoding.UTF8.GetString(buffer, 0, finalSize);
    }

    public async IAsyncEnumerable<string> StreamGenerationAsync(
        string modelId,
        string prompt,
        IReadOnlyList<MessageContent>? mediaParts,
        InferenceSettings settings,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var model = await _modelManager.AcquireModelAsync(modelId, ct);
        using var context = await _modelManager.AcquireContextAsync(modelId, ct);

        // РЕЗОЛВИМ МЕДИА ЧЕРЕЗ НОВЫЙ ИНТЕРФЕЙС
        using var mediaContext = mediaParts != null && mediaParts.Count > 0
            ? await _mediaResolver.ResolveMediaAsync(mediaParts, ct)
            : null;

        var localImagePaths = mediaContext?.LocalFilePaths;

        IntPtr nativeModelPtr = UnwrapModel(model);
        IntPtr ctxHandle = UnwrapContext(context.TextContext);
        IntPtr vocab = LlamaNative.llama_model_get_vocab(nativeModelPtr);
        IntPtr mtmdCtxHandle = context.VisionContext?.Handle is MtmdVisionHandle vh ? vh.Pointer : IntPtr.Zero;
        int currentPos = 0;

        // Phase 1: Prompt Evaluation (Unified)
        if (mtmdCtxHandle != IntPtr.Zero)
        {
            var bitmapHandles = new List<MtmdNative.MtmdBitmapHandle>();
            IntPtr[] bitmapPtrs = Array.Empty<IntPtr>();

            try
            {
                if (localImagePaths != null && localImagePaths.Count > 0)
                {
                    var opt = MtmdNative.mtmd_helper_init_opt_default();
                    bitmapPtrs = new IntPtr[localImagePaths.Count];

                    for (int i = 0; i < localImagePaths.Count; i++)
                    {
                        var bmpWrapper = MtmdNative.mtmd_helper_bitmap_init_from_file(
                            mtmdCtxHandle, localImagePaths[i], false, opt);

                        if (bmpWrapper.Bitmap == IntPtr.Zero)
                        {
                            throw new InvalidOperationException($"Failed to load image: {localImagePaths[i]}");
                        }

                        var handle = new MtmdNative.MtmdBitmapHandle(bmpWrapper.Bitmap);
                        bitmapHandles.Add(handle);
                        bitmapPtrs[i] = handle.DangerousGetHandle();
                    }
                }

                IntPtr rawHandle = MtmdNative.mtmd_input_chunks_init();
                using var chunksHandle = new MtmdNative.MtmdInputChunksHandle(rawHandle);

                IntPtr textPtr = Marshal.StringToCoTaskMemUTF8(prompt);
                try
                {
                    var inputText = new MtmdInputText
                    {
                        Text = textPtr,
                        TextLen = (UIntPtr)Encoding.UTF8.GetByteCount(prompt),
                        AddSpecial = true,
                        ParseSpecial = true
                    };

                    int tokResult = MtmdNative.mtmd_tokenize(
                        mtmdCtxHandle, chunksHandle, ref inputText, bitmapPtrs, (UIntPtr)bitmapPtrs.Length);

                    if (tokResult != 0)
                    {
                        throw new InvalidOperationException($"mtmd_tokenize failed with code: {tokResult}");
                    }

                    int evalResult = MtmdNative.mtmd_helper_eval_chunks(
                        mtmdCtxHandle,
                        ctxHandle,
                        chunksHandle,
                        0,
                        0,
                        settings.BatchSize > 0 ? settings.BatchSize : 512,
                        true,
                        out currentPos);

                    if (evalResult != 0)
                    {
                        throw new InvalidOperationException($"mtmd_helper_eval_chunks failed with code: {evalResult}");
                    }
                }
                finally
                {
                    Marshal.FreeCoTaskMem(textPtr);
                }
            }
            finally
            {
                foreach (var handle in bitmapHandles)
                {
                    handle.Dispose();
                }
            }
        }
        else
        {
            if (localImagePaths != null && localImagePaths.Count > 0)
            {
                _logger.LogWarning("Images provided, but no multimodal projector (mmproj) is loaded. Images will be ignored.");
            }

            int[] tokens = await TokenizeDataAsync(modelId, prompt, ct);

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

                        for (int j = 0; j < evalBatchSize; j++)
                        {
                            tokenPtr[j] = tokens[i + j];
                            posPtr[j] = i + j;
                            nSeqIdPtr[j] = 1;
                            seqIdPtr[j][0] = 0;
                            logitsPtr[j] = (byte)((i + j == tokens.Length - 1) ? 1 : 0);
                        }

                        batch.NTokens = evalBatchSize;

                        int evalResult = LlamaNative.llama_decode(ctxHandle, batch);
                        if (evalResult != 0)
                        {
                            throw new InvalidOperationException($"Prompt evaluation failed with code: {evalResult}");
                        }
                    }
                    currentPos = tokens.Length;
                }
                finally
                {
                    LlamaNative.llama_batch_free(batch);
                }
            }
        }

        // Phase 2: Generation Loop
        var chainParams = LlamaNative.llama_sampler_chain_default_params();
        IntPtr sampler = LlamaNative.llama_sampler_chain_init(chainParams);

        try
        {
            LlamaNative.llama_sampler_chain_add(sampler, LlamaNative.llama_sampler_init_top_k(settings.TopK > 0 ? settings.TopK : 40));
            LlamaNative.llama_sampler_chain_add(sampler, LlamaNative.llama_sampler_init_top_p(settings.TopP > 0 ? settings.TopP : 0.9f, 1));
            LlamaNative.llama_sampler_chain_add(sampler, LlamaNative.llama_sampler_init_temp(settings.Temperature > 0 ? settings.Temperature : 0.7f));

            uint activeSeed = settings.Seed ?? (uint)Random.Shared.Next();
            LlamaNative.llama_sampler_chain_add(sampler, LlamaNative.llama_sampler_init_dist(activeSeed));

            int eos = LlamaNative.llama_vocab_eos(vocab);
            int generated = 0;

            var utf8Decoder = Encoding.UTF8.GetDecoder();
            byte[] pieceBuffer = new byte[256];
            char[] charBuffer = new char[512];

            while (generated < settings.MaxTokens)
            {
                ct.ThrowIfCancellationRequested();

                int logitIndex = (generated == 0) ? -1 : 0;
                int token = LlamaNative.llama_sampler_sample(sampler, ctxHandle, logitIndex);

                if (token < 0 || LlamaNative.llama_vocab_is_eog(vocab, token))
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }
}