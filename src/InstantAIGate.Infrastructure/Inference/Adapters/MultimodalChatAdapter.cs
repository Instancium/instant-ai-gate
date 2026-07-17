using InstantAIGate.Application.Dtos.Requests;
using InstantAIGate.Application.Interfaces.Catalog;
using InstantAIGate.Application.Interfaces.Inference;
using InstantAIGate.Application.Interfaces.Storage;
using InstantAIGate.Infrastructure.Inference.Native;
using InstantAIGate.Infrastructure.Templates;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using InstantAIGate.Domain.Extensions;

namespace InstantAIGate.Infrastructure.Inference.Adapters
{
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

        public async Task<string> GenerateAsync(LlamaChatRequest request, CancellationToken ct = default)
        {
            var sb = new StringBuilder();
            await foreach (var token in StreamAsync(request, ct))
            {
                sb.Append(token);
            }
            return sb.ToString();
        }

        public async IAsyncEnumerable<string> StreamAsync(LlamaChatRequest request, [EnumeratorCancellation] CancellationToken ct = default)
        {
            string? imageUrl = ExtractFirstImageUrl(request.Messages);
            if (string.IsNullOrEmpty(imageUrl))
            {
                throw new InvalidOperationException("No image found in the multimodal request.");
            }

            var imageResult = await _imageResolver.ResolveAsync(imageUrl, ct);

            var manifest = await _modelRegistry.GetModelAsync(request.Model);
            if (manifest == null)
            {
                throw new InvalidOperationException("Manifest not found.");
            }

            var mainFile = manifest.GetMainTextFile();
            var projectorFile = manifest.GetVisionProjectorFile();

            if (mainFile == null || projectorFile == null)
            {
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
                throw new InvalidOperationException($"Configuration for model '{request.Model}' not found in active registry.");
            }

            int nBatchLimit = (int)modelSettings.BatchSize;
            if (nBatchLimit <= 0) nBatchLimit = 512;
            int maxTokens = request.MaxTokens > 0 ? request.MaxTokens : 512;

            IntPtr vocab = _llamaApi.ModelGetVocab(llamaModel.Handle);
            int nEmbdDim = _llamaApi.GetModelEmbeddingSize(llamaModel.Handle);

            GCHandle pinnedRgb = GCHandle.Alloc(imageResult.RgbData, GCHandleType.Pinned);

            IntPtr nativeBitmap = IntPtr.Zero;
            IntPtr nativeChunks = IntPtr.Zero;
            IntPtr[] bitmapArray = new IntPtr[1];

            var chainParams = _llamaApi.SamplerChainDefaultParams();
            IntPtr sampler = _llamaApi.SamplerChainInit(chainParams);
            _llamaApi.SamplerChainAdd(sampler, _llamaApi.SamplerInitTopK(request.TopK));
            _llamaApi.SamplerChainAdd(sampler, _llamaApi.SamplerInitTopP(request.TopP, (nuint)1));
            _llamaApi.SamplerChainAdd(sampler, _llamaApi.SamplerInitTemp(request.Temperature > 0 ? request.Temperature : 0.7f));
            _llamaApi.SamplerChainAdd(sampler, _llamaApi.SamplerInitDist(request.Seed.HasValue ? (uint)request.Seed.Value : (uint)Random.Shared.Next()));

            IntPtr repetitionSampler = _llamaApi.SamplerInitRepetition(1.1f, request.FrequencyPenalty, request.PresencePenalty);
            if (repetitionSampler != IntPtr.Zero)
            {
                _llamaApi.SamplerChainAdd(sampler, repetitionSampler);
            }

            int[] batchPos = new int[nBatchLimit];
            int[] batchNSeq = new int[nBatchLimit];
            sbyte[] batchLogits = new sbyte[nBatchLimit];

            int seqIdValue = 0;
            GCHandle hSeqIdValue = GCHandle.Alloc(seqIdValue, GCHandleType.Pinned);
            IntPtr[] seqIdPointers = new IntPtr[nBatchLimit];
            for (int i = 0; i < nBatchLimit; i++)
            {
                seqIdPointers[i] = hSeqIdValue.AddrOfPinnedObject();
            }

            GCHandle hPos = GCHandle.Alloc(batchPos, GCHandleType.Pinned);
            GCHandle hNSeq = GCHandle.Alloc(batchNSeq, GCHandleType.Pinned);
            GCHandle hLogits = GCHandle.Alloc(batchLogits, GCHandleType.Pinned);
            GCHandle hSeqIdPtrs = GCHandle.Alloc(seqIdPointers, GCHandleType.Pinned);

            var utf8Decoder = Encoding.UTF8.GetDecoder();
            byte[] byteBuffer = new byte[256];
            char[] charBuffer = new char[512];
            var accumulatedText = new StringBuilder();

            try
            {
                nativeBitmap = _mtmdApi.CreateBitmap(imageResult.Width, imageResult.Height, pinnedRgb.AddrOfPinnedObject());
                bitmapArray[0] = nativeBitmap;

                nativeChunks = _mtmdApi.CreateInputChunks();
                string prompt = profile.Template.BuildPrompt(request.Messages);

                int totalPromptTokens = _mtmdApi.Tokenize(mtmdContext.Handle, nativeChunks, prompt, bitmapArray);
                if (totalPromptTokens <= 0)
                {
                    throw new InvalidOperationException("Failed to tokenize multimodal prompt.");
                }

                int chunkCount = _mtmdApi.GetChunksCount(nativeChunks);
                int currentPos = 0;

                for (int c = 0; c < chunkCount; c++)
                {
                    ct.ThrowIfCancellationRequested();

                    IntPtr chunk = _mtmdApi.GetChunk(nativeChunks, c);
                    var chunkType = _mtmdApi.GetChunkType(chunk);
                    int nTokens = _mtmdApi.GetChunkTokenCount(chunk);
                    bool isLastChunk = (c == chunkCount - 1);

                    if (chunkType == InputChunkType.Text)
                    {
                        int[] chunkTokens = _mtmdApi.GetChunkTokens(chunk);
                        GCHandle hTokens = GCHandle.Alloc(chunkTokens, GCHandleType.Pinned);

                        try
                        {
                            for (int i = 0; i < nTokens; i++)
                            {
                                batchPos[i] = currentPos + i;
                                batchNSeq[i] = 1;
                                batchLogits[i] = (sbyte)((isLastChunk && i == nTokens - 1) ? 1 : 0);
                            }

                            int result = _llamaApi.Decode(
                                llamaContext.Handle,
                                nTokens,
                                hTokens.AddrOfPinnedObject(),
                                hPos.AddrOfPinnedObject(),
                                hNSeq.AddrOfPinnedObject(),
                                hSeqIdPtrs.AddrOfPinnedObject(),
                                hLogits.AddrOfPinnedObject());

                            if (result != 0)
                            {
                                throw new InvalidOperationException($"Decode failed for text chunk: {result}");
                            }
                        }
                        finally
                        {
                            hTokens.Free();
                        }
                    }
                    else if (chunkType == InputChunkType.Image)
                    {
                        if (_mtmdApi.EncodeChunk(mtmdContext.Handle, chunk) != 0)
                        {
                            throw new InvalidOperationException("Failed to encode image chunk.");
                        }

                        float[] embeddings = _mtmdApi.GetOutputEmbeddings(mtmdContext.Handle, nTokens, nEmbdDim);
                        GCHandle hEmbd = GCHandle.Alloc(embeddings, GCHandleType.Pinned);

                        try
                        {
                            for (int i = 0; i < nTokens; i++)
                            {
                                batchPos[i] = currentPos + i;
                                batchNSeq[i] = 1;
                                batchLogits[i] = (sbyte)((isLastChunk && i == nTokens - 1) ? 1 : 0);
                            }

                            int result = _llamaApi.DecodeEmbeddings(
                                llamaContext.Handle,
                                nTokens,
                                hEmbd.AddrOfPinnedObject(),
                                hPos.AddrOfPinnedObject(),
                                hNSeq.AddrOfPinnedObject(),
                                hSeqIdPtrs.AddrOfPinnedObject(),
                                hLogits.AddrOfPinnedObject());

                            if (result != 0)
                            {
                                throw new InvalidOperationException($"Decode failed for image chunk: {result}");
                            }
                        }
                        finally
                        {
                            hEmbd.Free();
                        }
                    }

                    currentPos += nTokens;
                }

                int eos = _llamaApi.VocabEos(vocab);
                int generated = 0;
                int[] singleTokenBuffer = new int[1];
                GCHandle hSingleToken = GCHandle.Alloc(singleTokenBuffer, GCHandleType.Pinned);

                try
                {
                    while (generated < maxTokens)
                    {
                        ct.ThrowIfCancellationRequested();

                        int token = _llamaApi.SamplerSample(sampler, llamaContext.Handle, 0);
                        if (token == eos || token < 0) break;

                        generated++;
                        singleTokenBuffer[0] = token;
                        batchPos[0] = currentPos++;
                        batchNSeq[0] = 1;
                        batchLogits[0] = 1;

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
                                string piece = new string(charBuffer, 0, charsDecoded);
                                accumulatedText.Append(piece);
                                yield return piece;
                            }
                        }

                        int result = _llamaApi.Decode(
                            llamaContext.Handle,
                            1,
                            hSingleToken.AddrOfPinnedObject(),
                            hPos.AddrOfPinnedObject(),
                            hNSeq.AddrOfPinnedObject(),
                            hSeqIdPtrs.AddrOfPinnedObject(),
                            hLogits.AddrOfPinnedObject());

                        if (result != 0)
                        {
                            throw new InvalidOperationException("Decode failed during generation.");
                        }
                    }
                }
                finally
                {
                    hSingleToken.Free();
                }
            }
            finally
            {
                if (nativeChunks != IntPtr.Zero) _mtmdApi.FreeInputChunks(nativeChunks);
                if (nativeBitmap != IntPtr.Zero) _mtmdApi.FreeBitmap(nativeBitmap);
                if (pinnedRgb.IsAllocated) pinnedRgb.Free();

                _llamaApi.SamplerFree(sampler);
                if (hPos.IsAllocated) hPos.Free();
                if (hNSeq.IsAllocated) hNSeq.Free();
                if (hLogits.IsAllocated) hLogits.Free();
                if (hSeqIdPtrs.IsAllocated) hSeqIdPtrs.Free();
                if (hSeqIdValue.IsAllocated) hSeqIdValue.Free();
            }
        }

        private string? ExtractFirstImageUrl(List<ChatMessage>? messages)
        {
            if (messages == null) return null;

            foreach (var message in messages)
            {
                if (message.ContentParts != null)
                {
                    var imagePart = message.ContentParts.FirstOrDefault(p => p.Type == "image_url" && p.ImageUrl != null);
                    if (imagePart != null)
                    {
                        return imagePart.ImageUrl!.Url;
                    }
                }
            }

            return null;
        }
    }
}