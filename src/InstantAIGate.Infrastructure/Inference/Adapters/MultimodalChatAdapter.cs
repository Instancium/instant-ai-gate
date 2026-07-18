using InstantAIGate.Application.Dtos.Requests;
using InstantAIGate.Application.Interfaces.Catalog;
using InstantAIGate.Application.Interfaces.Inference;
using InstantAIGate.Application.Interfaces.Storage;
using InstantAIGate.Domain.Extensions;
using InstantAIGate.Infrastructure.Inference.Native;
using InstantAIGate.Infrastructure.Inference.Vision;
using InstantAIGate.Infrastructure.Templates;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace InstantAIGate.Infrastructure.Inference.Adapters
{
    /// <summary>
    /// Adapter for processing multimodal chat requests using LLaMA and MTMD models.
    /// Handles image extraction, tokenization, and streaming inference.
    /// </summary>
    public class MultimodalChatAdapter : IChatAdapter
    {
        private readonly ILlamaModelManager _llamaManager;
        private readonly IMtmdClipModelManager _mtmdManager;
        private readonly IImageContentResolver _imageResolver;
        private readonly INativeMtmdApi _mtmdApi;
        private readonly INativeLlamaApi _llamaApi;
        private readonly IModelPathProvider _pathProvider;
        private readonly ILogger<MultimodalChatAdapter> _logger;
        private readonly IModelRegistry _modelRegistry;

        public MultimodalChatAdapter(
            ILlamaModelManager llamaManager,
            IMtmdClipModelManager mtmdManager,
            IImageContentResolver imageResolver,
            INativeMtmdApi mtmdApi,
            INativeLlamaApi llamaApi,
            IModelPathProvider pathProvider,
            ILogger<MultimodalChatAdapter> logger,
            IModelRegistry modelRegistry)
        {
            _llamaManager = llamaManager;
            _mtmdManager = mtmdManager;
            _imageResolver = imageResolver;
            _mtmdApi = mtmdApi;
            _llamaApi = llamaApi;
            _pathProvider = pathProvider;
            _logger = logger;
            _modelRegistry = modelRegistry;
        }

        /// <summary>
        /// Generates a complete text response for a multimodal chat request.
        /// </summary>
        /// <param name="request">The chat request containing messages and optional images.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The fully generated model response.</returns>
        public async Task<string> GenerateAsync(LlamaChatRequest request, CancellationToken ct = default)
        {
            StringBuilder sb = new StringBuilder();
            await foreach (string token in StreamAsync(request, ct))
            {
                sb.Append(token);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Streams the generated response tokens for a multimodal chat request.
        /// </summary>
        /// <param name="request">The chat request containing messages and optional images.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>An asynchronous stream of string tokens.</returns>
        public async IAsyncEnumerable<string> StreamAsync(LlamaChatRequest request, [EnumeratorCancellation] CancellationToken ct = default)
        {
            (string? imageUrl, List<ChatMessage> modifiedMessages) = PrepareMessagesWithImageMarker(request.Messages);

            if (string.IsNullOrEmpty(imageUrl))
            {
                _logger.LogError("Validation failed: No image found in the multimodal request.");
                throw new InvalidOperationException("No image found in the multimodal request.");
            }

            var imageResult = await _imageResolver.ResolveAsync(imageUrl, ct);

            if (string.IsNullOrEmpty(request.Model))
            {
                throw new ArgumentException("Model identifier cannot be null or empty.", nameof(request.Model));
            }

            var manifest = await _modelRegistry.GetModelAsync(request.Model);

            if (manifest == null)
            {
                _logger.LogError("Manifest lookup failed for model: {Model}", request.Model);
                throw new InvalidOperationException("Manifest not found.");
            }

            var mainFile = manifest.GetMainTextFile();
            var projectorFile = manifest.GetVisionProjectorFile();

            if (mainFile == null || projectorFile == null)
            {
                _logger.LogError("Multimodal package is incomplete for model: {Model}", request.Model);
                throw new InvalidOperationException("Multimodal package is missing either the text model or the vision projector file.");
            }

            string textModelPath = _pathProvider.GetModelFilePath(manifest.RepoId, mainFile.FileName);
            string projectorPath = _pathProvider.GetModelFilePath(manifest.RepoId, projectorFile.FileName);

            var profile = ModelProfileResolver.Resolve(textModelPath);

            using var llamaModel = await _llamaManager.AcquireModelAsync(request.Model, ct);
            using var llamaContext = await _llamaManager.AcquireContextAsync(request.Model, ct);

            using var mtmdContext = await _mtmdManager.AcquireContextAsync(
                projectorPath,
                llamaModel.Handle,
                useGpu: true,
                ct);

            if (!_llamaManager.ActiveModels.TryGetValue(request.Model, out var modelSettings))
            {
                _logger.LogError("Configuration for model '{Model}' not found in active registry.", request.Model);
                throw new InvalidOperationException($"Configuration for model '{request.Model}' not found in active registry.");
            }

            int nBatchLimit = (int)modelSettings.BatchSize > 0 ? (int)modelSettings.BatchSize : 512;
            int maxTokens = request.MaxTokens > 0 ? request.MaxTokens : 512;

            IntPtr vocab = _llamaApi.ModelGetVocab(llamaModel.Handle);

            GCHandle pinnedRgb = GCHandle.Alloc(imageResult.RgbData, GCHandleType.Pinned);
            IntPtr nativeBitmap = IntPtr.Zero;
            IntPtr nativeChunks = IntPtr.Zero;
            IntPtr[] bitmapArray = new IntPtr[1];

            NativeSamplerChainParams chainParams = _llamaApi.SamplerChainDefaultParams();
            IntPtr sampler = _llamaApi.SamplerChainInit(chainParams);
            _llamaApi.SamplerChainAdd(sampler, _llamaApi.SamplerInitTopK(request.TopK));
            _llamaApi.SamplerChainAdd(sampler, _llamaApi.SamplerInitTopP(request.TopP, 1));
            _llamaApi.SamplerChainAdd(sampler, _llamaApi.SamplerInitTemp(request.Temperature > 0 ? request.Temperature : 0.7f));
            _llamaApi.SamplerChainAdd(sampler, _llamaApi.SamplerInitDist(request.Seed.HasValue ? (uint)request.Seed.Value : (uint)Random.Shared.Next()));

            IntPtr repetitionSampler = _llamaApi.SamplerInitRepetition(1.1f, request.FrequencyPenalty, request.PresencePenalty);
            if (repetitionSampler != IntPtr.Zero)
            {
                _llamaApi.SamplerChainAdd(sampler, repetitionSampler);
            }

            Decoder utf8Decoder = Encoding.UTF8.GetDecoder();
            byte[] byteBuffer = new byte[256];
            char[] charBuffer = new char[512];

            try
            {
                nativeBitmap = _mtmdApi.CreateBitmap(imageResult.Width, imageResult.Height, pinnedRgb.AddrOfPinnedObject());
                if (nativeBitmap == IntPtr.Zero)
                {
                    _logger.LogError("Failed to create native bitmap for MTMD API.");
                    throw new InvalidOperationException("Failed to create native bitmap.");
                }

                bitmapArray[0] = nativeBitmap;
                nativeChunks = _mtmdApi.CreateInputChunks();

                string expectedMarker = _mtmdApi.GetExpectedImageMarker(mtmdContext.Handle);
                string prompt = profile.Template.BuildPrompt(modifiedMessages);

                prompt = prompt.Replace("{IMAGE_MARKER}", expectedMarker)
                               .Replace("<image>", expectedMarker);

                int firstIdx = prompt.IndexOf(expectedMarker);
                if (firstIdx == -1)
                {
                    prompt = expectedMarker + "\n" + prompt;
                }
                else
                {
                    int nextIdx = prompt.IndexOf(expectedMarker, firstIdx + expectedMarker.Length);
                    while (nextIdx != -1)
                    {
                        prompt = prompt.Remove(nextIdx, expectedMarker.Length);
                        nextIdx = prompt.IndexOf(expectedMarker, firstIdx + expectedMarker.Length);
                    }
                }

                int tokenizeResult = _mtmdApi.Tokenize(mtmdContext.Handle, nativeChunks, prompt, bitmapArray);
                if (tokenizeResult < 0)
                {
                    _logger.LogError("Tokenization failed. Native API returned {ResultCode}", tokenizeResult);
                    throw new InvalidOperationException($"Failed to tokenize multimodal prompt. Native API returned {tokenizeResult}. Marker '{expectedMarker}' might be rejected.");
                }

                int chunkCount = _mtmdApi.GetChunksCount(nativeChunks);
                int currentPos = 0;
                int lastEvalBatchSize = 0;

                for (int c = 0; c < chunkCount; c++)
                {
                    ct.ThrowIfCancellationRequested();

                    IntPtr chunk = _mtmdApi.GetChunk(nativeChunks, c);
                    var chunkType = _mtmdApi.GetChunkType(chunk);
                    int nTokens = _mtmdApi.GetChunkTokenCount(chunk);

                    bool isLastChunk = (c == chunkCount - 1);

                    if (chunkType == InputChunkType.Image)
                    {
                        IntPtr mtmdBatch = _mtmdApi.BatchInit(mtmdContext.Handle);

                        try
                        {
                            _mtmdApi.BatchAddChunk(mtmdBatch, chunk);
                            int encodeResult = _mtmdApi.BatchEncode(mtmdBatch);
                            if (encodeResult != 0)
                            {
                                _logger.LogError("Batch encode failed with code {Code}", encodeResult);
                                throw new InvalidOperationException($"Batch encode failed with code {encodeResult}");
                            }
                        }
                        finally
                        {
                            _mtmdApi.BatchFree(mtmdBatch);
                        }

                        currentPos += nTokens;
                        if (isLastChunk) lastEvalBatchSize = nTokens;
                    }
                    else if (chunkType == InputChunkType.Text)
                    {
                        int[] chunkTokens = _mtmdApi.GetChunkTokens(chunk);

                        for (int i = 0; i < nTokens; i += nBatchLimit)
                        {
                            int evalBatchSize = Math.Min(nTokens - i, nBatchLimit);
                            bool isLastEvalInChunk = (i + evalBatchSize == nTokens);

                            if (isLastChunk && isLastEvalInChunk) lastEvalBatchSize = evalBatchSize;

                            int[] batchTokens = new int[evalBatchSize];
                            int[] batchPos = new int[evalBatchSize];
                            int[] batchNSeq = new int[evalBatchSize];
                            sbyte[] batchLogits = new sbyte[evalBatchSize];

                            int seqIdValue = 0;
                            GCHandle hSeqIdValue = GCHandle.Alloc(seqIdValue, GCHandleType.Pinned);
                            IntPtr[] seqIdPointers = new IntPtr[evalBatchSize];

                            for (int j = 0; j < evalBatchSize; j++)
                            {
                                batchTokens[j] = chunkTokens[i + j];
                                batchPos[j] = currentPos + j;
                                batchNSeq[j] = 1;
                                batchLogits[j] = (sbyte)((isLastChunk && isLastEvalInChunk && j == evalBatchSize - 1) ? 1 : 0);
                                seqIdPointers[j] = hSeqIdValue.AddrOfPinnedObject();
                            }

                            GCHandle hTokens = GCHandle.Alloc(batchTokens, GCHandleType.Pinned);
                            GCHandle hPos = GCHandle.Alloc(batchPos, GCHandleType.Pinned);
                            GCHandle hNSeq = GCHandle.Alloc(batchNSeq, GCHandleType.Pinned);
                            GCHandle hLogits = GCHandle.Alloc(batchLogits, GCHandleType.Pinned);
                            GCHandle hSeqIdPtrs = GCHandle.Alloc(seqIdPointers, GCHandleType.Pinned);

                            try
                            {
                                int result = _llamaApi.Decode(
                                    llamaContext.Handle,
                                    evalBatchSize,
                                    hTokens.AddrOfPinnedObject(),
                                    hPos.AddrOfPinnedObject(),
                                    hNSeq.AddrOfPinnedObject(),
                                    hSeqIdPtrs.AddrOfPinnedObject(),
                                    hLogits.AddrOfPinnedObject());

                                if (result != 0)
                                {
                                    _logger.LogError("Decode failed for text chunk. Native API returned {ResultCode}", result);
                                    throw new InvalidOperationException($"Decode failed for text chunk: {result}");
                                }
                            }
                            finally
                            {
                                hTokens.Free();
                                hPos.Free();
                                hNSeq.Free();
                                hLogits.Free();
                                hSeqIdPtrs.Free();
                                hSeqIdValue.Free();
                            }
                            currentPos += evalBatchSize;
                        }
                    }
                }

                int eos = _llamaApi.VocabEos(vocab);
                int generated = 0;

                int[] singleTokenBuffer = new int[1];
                GCHandle hSingleToken = GCHandle.Alloc(singleTokenBuffer, GCHandleType.Pinned);
                int genSeqId = 0;
                GCHandle hGenSeqId = GCHandle.Alloc(genSeqId, GCHandleType.Pinned);

                try
                {
                    while (generated < maxTokens)
                    {
                        ct.ThrowIfCancellationRequested();

                        int logitIndex = (generated == 0) ? (lastEvalBatchSize - 1) : 0;
                        int token = _llamaApi.SamplerSample(sampler, llamaContext.Handle, logitIndex);

                        if (token == eos || token < 0) break;

                        generated++;
                        singleTokenBuffer[0] = token;

                        int[] genBatchPos = new int[1] { currentPos++ };
                        int[] genBatchNSeq = new int[1] { 1 };
                        sbyte[] genBatchLogits = new sbyte[1] { 1 };

                        IntPtr[] genSeqIdPtrs = new IntPtr[1] { hGenSeqId.AddrOfPinnedObject() };

                        GCHandle hGenPos = GCHandle.Alloc(genBatchPos, GCHandleType.Pinned);
                        GCHandle hGenNSeq = GCHandle.Alloc(genBatchNSeq, GCHandleType.Pinned);
                        GCHandle hGenLogits = GCHandle.Alloc(genBatchLogits, GCHandleType.Pinned);
                        GCHandle hGenSeqIdPtrs = GCHandle.Alloc(genSeqIdPtrs, GCHandleType.Pinned);

                        try
                        {
                            int pieceSize = _llamaApi.TokenToPiece(vocab, token, byteBuffer, byteBuffer.Length, 0, false);
                            if (pieceSize > byteBuffer.Length)
                            {
                                byteBuffer = new byte[pieceSize];
                                pieceSize = _llamaApi.TokenToPiece(vocab, token, byteBuffer, byteBuffer.Length, 0, false);
                            }

                            if (pieceSize > 0)
                            {
                                int charsDecoded = utf8Decoder.GetChars(byteBuffer, 0, pieceSize, charBuffer, 0, flush: false);
                                if (charsDecoded > 0)
                                {
                                    yield return new string(charBuffer, 0, charsDecoded);
                                }
                            }

                            int result = _llamaApi.Decode(
                                llamaContext.Handle,
                                1,
                                hSingleToken.AddrOfPinnedObject(),
                                hGenPos.AddrOfPinnedObject(),
                                hGenNSeq.AddrOfPinnedObject(),
                                hGenSeqIdPtrs.AddrOfPinnedObject(),
                                hGenLogits.AddrOfPinnedObject());

                            if (result != 0)
                            {
                                _logger.LogError("Decode failed during generation loop. API returned {ResultCode}", result);
                                throw new InvalidOperationException("Decode failed during generation.");
                            }
                        }
                        finally
                        {
                            hGenPos.Free();
                            hGenNSeq.Free();
                            hGenLogits.Free();
                            hGenSeqIdPtrs.Free();
                        }
                    }
                }
                finally
                {
                    hGenSeqId.Free();
                    hSingleToken.Free();
                    utf8Decoder.GetChars(Array.Empty<byte>(), 0, 0, charBuffer, 0, flush: true);
                }
            }
            finally
            {
                if (nativeChunks != IntPtr.Zero) _mtmdApi.FreeInputChunks(nativeChunks);
                if (nativeBitmap != IntPtr.Zero) _mtmdApi.FreeBitmap(nativeBitmap);
                if (pinnedRgb.IsAllocated) pinnedRgb.Free();

                _llamaApi.SamplerFree(sampler);
            }
        }

        private (string? Url, List<ChatMessage> ModifiedMessages) PrepareMessagesWithImageMarker(List<ChatMessage>? messages)
        {
            if (messages == null) return (null, new List<ChatMessage>());

            string? foundUrl = null;
            List<ChatMessage> modifiedMessages = new List<ChatMessage>();

            ChatMessage? systemMsg = messages.FirstOrDefault(m => m.Role == "system");
            if (systemMsg != null) modifiedMessages.Add(systemMsg);

            foreach (ChatMessage message in messages)
            {
                if (message.Role == "system") continue;

                if (message.ContentParts != null && message.ContentParts.Any(p => p.Type == "image_url"))
                {
                    List<ContentPart> newParts = new List<ContentPart>();

                    foreach (ContentPart part in message.ContentParts)
                    {
                        if (part.Type == "image_url" && part.ImageUrl != null)
                        {
                            foundUrl ??= part.ImageUrl.Url;
                            newParts.Add(new ContentPart { Type = "text", Text = "{IMAGE_MARKER}" });
                        }
                        else
                        {
                            newParts.Add(part);
                        }
                    }

                    modifiedMessages.Add(new ChatMessage
                    {
                        Role = message.Role,
                        Name = message.Name,
                        Content = "{IMAGE_MARKER}\n" + (message.Content ?? string.Empty),
                        ContentParts = newParts
                    });
                }
                else
                {
                    modifiedMessages.Add(message);
                }
            }

            return (foundUrl, modifiedMessages);
        }
    }
}