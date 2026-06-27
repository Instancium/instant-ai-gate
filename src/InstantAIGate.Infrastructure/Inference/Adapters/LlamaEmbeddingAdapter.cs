using InstantAIGate.Application.Interfaces.Inference;
using InstantAIGate.Application.Interfaces.Storage;
using InstantAIGate.Domain.Dtos.Config;
using InstantAIGate.Infrastructure.Inference.Native;
using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;
using System.Text;


namespace InstantAIGate.Infrastructure.Inference.Adapters
{
    /// <summary>
    /// Adapter for generating dense text embeddings using LLaMA models.
    /// Fully isolated from native P/Invoke calls through INativeLlamaApi.
    /// </summary>
    public class LlamaEmbeddingAdapter : IEmbeddingAdapter
    {
        private readonly IModelManager _modelManager;
        private readonly IModelPathProvider _pathProvider;
        private readonly INativeLlamaApi _nativeApi;
        private readonly ILogger<LlamaEmbeddingAdapter> _logger;

        public LlamaEmbeddingAdapter(
            IModelManager modelManager,
            IModelPathProvider pathProvider,
            INativeLlamaApi nativeApi,
            ILogger<LlamaEmbeddingAdapter> logger)
        {
            _modelManager = modelManager ?? throw new ArgumentNullException(nameof(modelManager));
            _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
            _nativeApi = nativeApi ?? throw new ArgumentNullException(nameof(nativeApi));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<IReadOnlyList<float[]>> GetEmbeddingAsync(string model, List<string> inputs, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(model))
                throw new ArgumentException("Model target identifier must be specified.", nameof(model));

            if (inputs == null || inputs.Count == 0)
                return Array.Empty<float[]>();

            var activeConfig = _modelManager.ActiveModels.FirstOrDefault(x => x.Key == model);
            if (activeConfig.Value is not ModelSettings settings)
                throw new InvalidOperationException($"Configuration settings for active model '{model}' could not be resolved.");

            using var weightsLease = await _modelManager.AcquireModelAsync(model, ct);
            if (weightsLease is not LlamaModel modelWrapper)
                throw new InvalidOperationException("Incompatible weights handle variant.");

            IntPtr modelHandle = modelWrapper.Handle;
            int embeddingLength = _nativeApi.ModelNEmbd(modelHandle);
            if (embeddingLength <= 0)
                throw new InvalidOperationException("The requested model does not support or export dense embedding layers.");

            IntPtr vocab = _nativeApi.ModelGetVocab(modelHandle);

            var flashAttn = settings.FlashAttention ? NativeLlamaFlashAttnType.Enabled : NativeLlamaFlashAttnType.Disabled;
            uint nCtx = (uint)(settings.ContextSize > 0 ? settings.ContextSize : 2048);
            uint nBatch = (uint)(settings.BatchSize > 0 ? settings.BatchSize : 512);
            int nThreads = settings.Threads > 0 ? settings.Threads : Environment.ProcessorCount;

            IntPtr ctxPtr = _nativeApi.CreateEmbeddingContext(modelHandle, nCtx, nBatch, nThreads, flashAttn);
            if (ctxPtr == IntPtr.Zero)
                throw new InvalidOperationException("Failed to establish unmanaged embedding execution infrastructure grid layers.");

            using var embeddingContext = new LlamaContext(ctxPtr, returnToPool: null);

            var results = new List<float[]>(inputs.Count);

            try
            {
                foreach (var text in inputs)
                {
                    ct.ThrowIfCancellationRequested();
                    if (string.IsNullOrEmpty(text)) continue;

                    byte[] textBytes = Encoding.UTF8.GetBytes(text);
                    int estimatedTokens = textBytes.Length + 4;
                    int[] tokens = new int[estimatedTokens];

                    int nTokens = _nativeApi.Tokenize(
                        vocab,
                        text,
                        textBytes.Length,
                        tokens,
                        tokens.Length,
                        true,
                        true);

                    if (nTokens < 0)
                    {
                        nTokens = Math.Abs(nTokens);
                        Array.Resize(ref tokens, nTokens);
                        nTokens = _nativeApi.Tokenize(vocab, text, textBytes.Length, tokens, tokens.Length, true, true);
                    }

                    if (nTokens <= 0) continue;

                    if (nTokens > nCtx)
                    {
                        _logger.LogWarning("Input text exceeds context size ({Tokens}/{Limit}). Truncating to context limit.", nTokens, nCtx);
                        nTokens = (int)nCtx;
                    }

                    int maxBatchSize = (int)nBatch;
                    int[] batchTokens = new int[maxBatchSize];
                    int[] batchPos = new int[maxBatchSize];
                    int[] batchNSeqId = new int[maxBatchSize];
                    sbyte[] batchLogits = new sbyte[maxBatchSize];

                    int[] seqIdArray = { 0 };
                    GCHandle hSeqIdArray = GCHandle.Alloc(seqIdArray, GCHandleType.Pinned);
                    IntPtr seqIdPtr = hSeqIdArray.AddrOfPinnedObject();

                    IntPtr[] seqIdPtrs = new IntPtr[maxBatchSize];
                    for (int i = 0; i < maxBatchSize; i++) seqIdPtrs[i] = seqIdPtr;

                    GCHandle hTokens = GCHandle.Alloc(batchTokens, GCHandleType.Pinned);
                    GCHandle hPos = GCHandle.Alloc(batchPos, GCHandleType.Pinned);
                    GCHandle hNSeqId = GCHandle.Alloc(batchNSeqId, GCHandleType.Pinned);
                    GCHandle hLogits = GCHandle.Alloc(batchLogits, GCHandleType.Pinned);
                    GCHandle hSeqIds = GCHandle.Alloc(seqIdPtrs, GCHandleType.Pinned);

                    try
                    {
                        _nativeApi.ClearMemory(_nativeApi.GetMemory(embeddingContext.Handle), true);

                        float[] aggregatedSentenceVector = new float[embeddingLength];
                        int validVectorsCount = 0;
                        bool decodeFailed = false;

                        for (int i = 0; i < nTokens; i += maxBatchSize)
                        {
                            ct.ThrowIfCancellationRequested();

                            int evalSize = Math.Min(nTokens - i, maxBatchSize);

                            for (int j = 0; j < evalSize; j++)
                            {
                                batchTokens[j] = tokens[i + j];
                                batchPos[j] = i + j;
                                batchNSeqId[j] = 1;
                                batchLogits[j] = 1;
                            }

                            int decodeStatus = _nativeApi.Decode(
                                embeddingContext.Handle,
                                evalSize,
                                hTokens.AddrOfPinnedObject(),
                                hPos.AddrOfPinnedObject(),
                                hNSeqId.AddrOfPinnedObject(),
                                hSeqIds.AddrOfPinnedObject(),
                                hLogits.AddrOfPinnedObject());

                            if (decodeStatus != 0)
                            {
                                _logger.LogError("Embedding execution tracking failed on chunk. Error code: {Code}", decodeStatus);
                                decodeFailed = true;
                                break;
                            }

                            for (int j = 0; j < evalSize; j++)
                            {
                                IntPtr ptrEmbeddingsIth = _nativeApi.GetEmbeddingsIth(embeddingContext.Handle, j);
                                if (ptrEmbeddingsIth != IntPtr.Zero)
                                {
                                    float[] tokenVector = new float[embeddingLength];
                                    Marshal.Copy(ptrEmbeddingsIth, tokenVector, 0, embeddingLength);

                                    for (int v = 0; v < embeddingLength; v++)
                                        aggregatedSentenceVector[v] += tokenVector[v];

                                    validVectorsCount++;
                                }
                            }
                        }

                        if (decodeFailed)
                            continue;

                        if (validVectorsCount == 0)
                        {
                            _logger.LogWarning("No embeddings were extracted for input text. Skipping normalization.");
                            results.Add(aggregatedSentenceVector);
                            continue;
                        }

                        for (int v = 0; v < embeddingLength; v++)
                            aggregatedSentenceVector[v] /= validVectorsCount;

                        float magnitude = 0f;
                        for (int v = 0; v < embeddingLength; v++)
                            magnitude += aggregatedSentenceVector[v] * aggregatedSentenceVector[v];
                        magnitude = MathF.Sqrt(magnitude);

                        if (magnitude > 1e-12f)
                        {
                            for (int v = 0; v < embeddingLength; v++)
                                aggregatedSentenceVector[v] /= magnitude;
                        }

                        results.Add(aggregatedSentenceVector);
                    }
                    finally
                    {
                        hTokens.Free();
                        hPos.Free();
                        hNSeqId.Free();
                        hLogits.Free();
                        hSeqIds.Free();
                        hSeqIdArray.Free();
                    }
                }

                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Dense text embedding vectorization failed for model: {RepoId}.", model);
                throw;
            }
        }
    }
}