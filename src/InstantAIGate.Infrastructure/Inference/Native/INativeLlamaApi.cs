namespace InstantAIGate.Infrastructure.Inference.Native;

/// <summary>
/// Abstraction layer over native llama.cpp P/Invoke calls.
/// Isolates all direct NativeLlamaMethods invocations to simplify future library updates.
/// </summary>
public interface INativeLlamaApi
{
    /// <summary>
    /// Loads all available backend implementations (CUDA, Metal, Vulkan, etc.).
    /// Must be called before any model operations to enable hardware acceleration.
    /// </summary>
    void LoadAllBackends();

    /// <summary>
    /// Initializes the backend system after loading.
    /// Prepares GPU contexts and memory allocators for inference operations.
    /// </summary>
    void BackendInit();

    /// <summary>
    /// Frees all backend resources and releases GPU memory.
    /// Should be called during application shutdown to prevent memory leaks.
    /// </summary>
    void BackendFree();

    /// <summary>
    /// Checks if the current build supports GPU offloading.
    /// Returns true if CUDA, Metal, or Vulkan backends are available and functional.
    /// </summary>
    bool SupportsGpuOffload();

    /// <summary>
    /// Sets a callback function for receiving log messages from llama.cpp.
    /// Useful for debugging and monitoring model operations.
    /// </summary>
    void SetLogCallback(NativeLogCallback callback);

    /// <summary>
    /// Retrieves the memory context associated with a model or context.
    /// Used for memory management and optimization operations.
    /// </summary>
    IntPtr GetMemory(IntPtr context);

    /// <summary>
    /// Clears the memory context, optionally releasing all allocated resources.
    /// Useful for resetting state between inference operations.
    /// </summary>
    void ClearMemory(IntPtr memory, bool clear);

    /// <summary>
    /// Frees the model and releases all associated resources.
    /// The model handle becomes invalid after this call.
    /// </summary>
    void FreeModel(IntPtr model);

    /// <summary>
    /// Frees the context and releases all associated resources.
    /// The context handle becomes invalid after this call.
    /// </summary>
    void FreeContext(IntPtr context);

    /// <summary>
    /// Loads a GGUF model from the specified file path with the given configuration.
    /// </summary>
    /// <param name="path">The file system path to the GGUF model file.</param>
    /// <param name="gpuLayers">Number of model layers to offload to GPU. 0 = CPU only.</param>
    /// <param name="mainGpu">The GPU device index to use as the primary device.</param>
    /// <param name="useMlock">If true, locks model memory in RAM to prevent swapping.</param>
    /// <param name="useMmap">If true, uses memory mapping for efficient file loading.</param>
    /// <param name="splitMode">Controls how the model is split across multiple GPUs.</param>
    /// <returns>A handle to the loaded model, or IntPtr.Zero on failure.</returns>
    IntPtr LoadModel(
        string path,
        int gpuLayers,
        int mainGpu,
        bool useMlock,
        bool useMmap,
        NativeLlamaSplitMode splitMode);

    /// <summary>
    /// Creates an inference context for the specified model with the given parameters.
    /// </summary>
    /// <param name="model">The model handle to create a context for.</param>
    /// <param name="nCtx">The context size in tokens (maximum sequence length).</param>
    /// <param name="nBatch">The batch size for prompt processing.</param>
    /// <param name="nThreads">Number of CPU threads to use for inference.</param>
    /// <param name="embeddings">If true, enables embedding extraction mode.</param>
    /// <param name="flashAttn">The flash attention type to use (none, v1, v2).</param>
    /// <param name="kvType">The data type for KV cache (f16, q8_0, q4_0, etc.).</param>
    /// <param name="offloadKqv">If true, offloads KV cache to GPU memory.</param>
    /// <returns>A handle to the created context, or IntPtr.Zero on failure.</returns>
    IntPtr CreateContext(
        IntPtr model,
        uint nCtx,
        uint nBatch,
        int nThreads,
        bool embeddings,
        NativeLlamaFlashAttnType flashAttn,
        NativeGgmlType kvType,
        bool offloadKqv);

    // === Tokenization ===

    /// <summary>
    /// Retrieves the vocabulary handle from the model.
    /// Required for tokenization and token-to-text conversion operations.
    /// </summary>
    IntPtr ModelGetVocab(IntPtr model);

    /// <summary>
    /// Tokenizes the input text into a sequence of token IDs.
    /// </summary>
    /// <param name="vocab">The vocabulary handle from the model.</param>
    /// <param name="text">The input text to tokenize.</param>
    /// <param name="textLen">The length of the input text in bytes.</param>
    /// <param name="tokens">Output array to store the token IDs.</param>
    /// <param name="maxTokens">Maximum number of tokens to write to the output array.</param>
    /// <param name="addSpecial">If true, adds special tokens (BOS/EOS) as appropriate.</param>
    /// <param name="parseSpecial">If true, parses special tokens in the input text.</param>
    /// <returns>The number of tokens written, or negative value if buffer is too small.</returns>
    int Tokenize(IntPtr vocab, string text, int textLen, int[] tokens, int maxTokens, bool addSpecial, bool parseSpecial);

    /// <summary>
    /// Retrieves the End-Of-Sequence (EOS) token ID from the vocabulary.
    /// Used to detect when the model has finished generating a response.
    /// </summary>
    int VocabEos(IntPtr vocab);

    /// <summary>
    /// Converts a token ID to its UTF-8 byte representation.
    /// </summary>
    /// <param name="vocab">The vocabulary handle from the model.</param>
    /// <param name="token">The token ID to convert.</param>
    /// <param name="buffer">Output buffer to store the UTF-8 bytes.</param>
    /// <param name="bufferSize">Size of the output buffer in bytes.</param>
    /// <param name="lstrip">Number of leading whitespace characters to strip.</param>
    /// <param name="special">If true, allows conversion of special tokens.</param>
    /// <returns>The number of bytes written, or negative value if buffer is too small.</returns>
    int TokenToPiece(IntPtr vocab, int token, byte[] buffer, int bufferSize, int lstrip, bool special);

    // === Sampler ===

    /// <summary>
    /// Retrieves the default parameters for creating a sampler chain.
    /// </summary>
    NativeSamplerChainParams SamplerChainDefaultParams();

    /// <summary>
    /// Initializes a new sampler chain with the given parameters.
    /// A sampler chain is a sequence of sampling operations applied in order.
    /// </summary>
    IntPtr SamplerChainInit(NativeSamplerChainParams @params);

    /// <summary>
    /// Adds a sampler to the chain. Samplers are applied in the order they are added.
    /// </summary>
    void SamplerChainAdd(IntPtr chain, IntPtr sampler);

    /// <summary>
    /// Creates a Top-K sampler that limits token selection to the K most likely candidates.
    /// </summary>
    /// <param name="k">Number of top tokens to consider. Lower values increase determinism.</param>
    /// <returns>A handle to the created sampler.</returns>
    IntPtr SamplerInitTopK(int k);

    /// <summary>
    /// Creates a Top-P (nucleus) sampler that limits token selection by cumulative probability.
    /// </summary>
    /// <param name="p">Cumulative probability threshold (0.0 to 1.0). Lower values increase focus.</param>
    /// <param name="minKeep">Minimum number of tokens to keep regardless of probability.</param>
    /// <returns>A handle to the created sampler.</returns>
    IntPtr SamplerInitTopP(float p, nuint minKeep);

    /// <summary>
    /// Creates a temperature sampler that controls output randomness.
    /// </summary>
    /// <param name="temp">Temperature value. Higher values increase randomness, lower values increase determinism.</param>
    /// <returns>A handle to the created sampler.</returns>
    IntPtr SamplerInitTemp(float temp);

    /// <summary>
    /// Creates a distribution sampler with the specified random seed.
    /// </summary>
    /// <param name="seed">Random seed for deterministic sampling. Same seed produces same output.</param>
    /// <returns>A handle to the created sampler.</returns>
    IntPtr SamplerInitDist(uint seed);

    /// <summary>
    /// Creates a repetition penalty sampler that discourages token repetition.
    /// </summary>
    /// <param name="penaltyRepeat">Penalty factor for repeated tokens. 1.0 = no penalty, >1.0 = discourage repetition.</param>
    /// <param name="penaltyFreq">Frequency-based penalty factor. Penalizes tokens based on occurrence count.</param>
    /// <param name="penaltyPresent">Presence-based penalty factor. Penalizes tokens that have appeared at all.</param>
    /// <returns>A handle to the created sampler.</returns>
    IntPtr SamplerInitRepetition(
        float penaltyRepeat,
        float penaltyFreq,
        float penaltyPresent);

    /// <summary>
    /// Samples the next token from the model's output logits using the configured sampler chain.
    /// </summary>
    /// <param name="sampler">The sampler chain handle.</param>
    /// <param name="context">The inference context handle.</param>
    /// <param name="index">The token index to sample from (usually last token in batch).</param>
    /// <returns>The sampled token ID.</returns>
    int SamplerSample(IntPtr sampler, IntPtr context, int index);

    /// <summary>
    /// Frees the sampler chain and releases all associated resources.
    /// </summary>
    void SamplerFree(IntPtr sampler);

    // === Inference ===

    /// <summary>
    /// Decodes a batch of tokens through the model to generate logits.
    /// </summary>
    /// <param name="context">The inference context handle.</param>
    /// <param name="batchSize">Number of tokens in the batch.</param>
    /// <param name="tokenPtr">Pointer to the array of token IDs.</param>
    /// <param name="posPtr">Pointer to the array of position IDs for each token.</param>
    /// <param name="nSeqIdPtr">Pointer to the array of sequence ID counts for each token.</param>
    /// <param name="seqIdPtr">Pointer to the array of sequence ID pointers for each token.</param>
    /// <param name="logitsPtr">Pointer to the array indicating which tokens should output logits.</param>
    /// <returns>0 on success, non-zero on failure.</returns>
    int Decode(IntPtr context, int batchSize, IntPtr tokenPtr, IntPtr posPtr, IntPtr nSeqIdPtr, IntPtr seqIdPtr, IntPtr logitsPtr);

    // === Embeddings ===

    /// <summary>
    /// Retrieves the embedding dimension size from the model.
    /// </summary>
    public int ModelNEmbd(IntPtr model) => NativeLlamaMethods.llama_model_n_embd(model);

    /// <summary>
    /// Retrieves the embedding vector for the specified token position.
    /// </summary>
    public IntPtr GetEmbeddingsIth(IntPtr context, int i) => NativeLlamaMethods.llama_get_embeddings_ith(context, i);

    /// <summary>
    /// Creates a specialized context for embedding extraction with the given parameters.
    /// </summary>
    public IntPtr CreateEmbeddingContext(IntPtr model, uint nCtx, uint nBatch, int nThreads, NativeLlamaFlashAttnType flashAttn)
    {
        var p = NativeLlamaMethods.llama_context_default_params();
        p.n_ctx = nCtx;
        p.n_batch = nBatch;
        p.n_ubatch = nBatch;
        p.n_threads = nThreads;
        p.n_threads_batch = nThreads;
        p.embeddings = true;
        p.pooling_type = NativeLlamaMethods.llama_pooling_type.LLAMA_POOLING_TYPE_NONE;
        p.flash_attn_type = (NativeLlamaMethods.llama_flash_attn_type)flashAttn;

        return NativeLlamaMethods.llama_init_from_model(model, p);
    }

    int GetModelEmbeddingSize(IntPtr model);

    /// <summary>
    /// Decodes a batch of embeddings (for multimodal vision) through the model.
    /// </summary>
    /// <param name="context">The inference context handle.</param>
    /// <param name="batchSize">Number of tokens/embeddings in the batch.</param>
    /// <param name="embdPtr">Pointer to the array of float embeddings.</param>
    /// <param name="posPtr">Pointer to the array of position IDs.</param>
    /// <param name="nSeqIdPtr">Pointer to the array of sequence ID counts.</param>
    /// <param name="seqIdPtr">Pointer to the array of sequence ID pointers.</param>
    /// <param name="logitsPtr">Pointer to the array indicating which tokens should output logits.</param>
    /// <returns>0 on success, non-zero on failure.</returns>
    int DecodeEmbeddings(IntPtr context, int batchSize, IntPtr embdPtr, IntPtr posPtr, IntPtr nSeqIdPtr, IntPtr seqIdPtr, IntPtr logitsPtr);
}