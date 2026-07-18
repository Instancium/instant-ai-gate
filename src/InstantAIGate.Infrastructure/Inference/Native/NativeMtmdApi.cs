using System;
using System.Runtime.InteropServices;

namespace InstantAIGate.Infrastructure.Inference.Native
{
    /// <summary>
    /// Represents the multimodal capabilities of a loaded projector model.
    /// </summary>
    public record MultimodalCapabilities(bool SupportsVision, bool SupportsAudio);

    /// <summary>
    /// Represents the type of a multimodal input chunk.
    /// </summary>
    public enum InputChunkType
    {
        Text = 0,
        Image = 1,
        Audio = 2
    }


    /// <summary>
    /// Provides a managed abstraction layer over the native multimodal (MTMD) API.
    /// Encapsulates all native structures and enums to prevent abstraction leaks.
    /// </summary>
    public class NativeMtmdApi : INativeMtmdApi
    {
        /// <summary>
        /// Retrieves the capabilities of the specified multimodal projector.
        /// </summary>
        /// <param name="projectorPath">Path to the mmproj file.</param>
        /// <returns>Capabilities of the multimodal model.</returns>
        public MultimodalCapabilities GetCapabilities(string projectorPath)
        {
            if (string.IsNullOrWhiteSpace(projectorPath))
            {
                throw new ArgumentException("Projector path cannot be null or empty.", nameof(projectorPath));
            }

            var nativeCaps = NativeMtmdMethods.GetCapFromFile(projectorPath);

            return new MultimodalCapabilities(
                SupportsVision: nativeCaps.InpVision,
                SupportsAudio: nativeCaps.InpAudio);
        }

        /// <summary>
        /// Retrieves the expected image marker string from the specified multimodal context.
        /// Returns a fallback "<image>" marker if the provided context pointer is zero, the native marker pointer is unallocated, or if string marshaling fails.
        /// </summary>
        /// <param name="mtmdContext">A pointer to the initialized native multimodal context.</param>
        /// <returns>The string representation of the expected image marker.</returns>

        public string GetExpectedImageMarker(IntPtr mtmdContext)
        {
            if (mtmdContext == IntPtr.Zero) return "<image>";

            IntPtr markerPtr = NativeMtmdMethods.GetMarker(mtmdContext); 

            if (markerPtr == IntPtr.Zero) return "<image>"; 

            return Marshal.PtrToStringAnsi(markerPtr) ?? "<image>";
        }

        /// <summary>
        /// Initializes a new multimodal context.
        /// </summary>
        /// <param name="projectorPath">Path to the mmproj file.</param>
        /// <param name="textModelPtr">Pointer to the underlying text model.</param>
        /// <param name="useGpu">Determines whether to utilize GPU acceleration.</param>
        /// <returns>Pointer to the initialized multimodal context.</returns>
        public IntPtr InitializeContext(string projectorPath, IntPtr textModelPtr, bool useGpu)
        {
            if (string.IsNullOrWhiteSpace(projectorPath))
            {
                throw new ArgumentException("Projector path cannot be null or empty.", nameof(projectorPath));
            }

            if (textModelPtr == IntPtr.Zero)
            {
                throw new ArgumentException("Text model pointer cannot be null.", nameof(textModelPtr));
            }

            var p = NativeMtmdMethods.GetDefaultContextParams();
            p.UseGpu = useGpu;
            p.NThreads = Environment.ProcessorCount;

            IntPtr ctx = NativeMtmdMethods.InitFromFile(projectorPath, textModelPtr, p);
            if (ctx == IntPtr.Zero)
            {
                throw new InvalidOperationException($"Failed to initialize MTMD context from file: {projectorPath}");
            }

            return ctx;
        }

        /// <summary>
        /// Frees the allocated multimodal context.
        /// </summary>
        /// <param name="mtmdContext">Pointer to the multimodal context.</param>
        public void FreeContext(IntPtr mtmdContext)
        {
            if (mtmdContext != IntPtr.Zero)
            {
                NativeMtmdMethods.Free(mtmdContext);
            }
        }

        /// <summary>
        /// Creates a native bitmap representation from RGB data.
        /// </summary>
        /// <param name="width">Bitmap width.</param>
        /// <param name="height">Bitmap height.</param>
        /// <param name="rgbData">Pointer to the raw RGB data.</param>
        /// <returns>Pointer to the created native bitmap.</returns>
        public IntPtr CreateBitmap(uint width, uint height, IntPtr rgbData)
        {
            if (rgbData == IntPtr.Zero)
            {
                throw new ArgumentException("RGB data pointer cannot be null.", nameof(rgbData));
            }

            IntPtr bitmap = NativeMtmdMethods.BitmapInit(width, height, rgbData);
            if (bitmap == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to initialize MTMD bitmap.");
            }

            return bitmap;
        }

        /// <summary>
        /// Frees the allocated native bitmap.
        /// </summary>
        /// <param name="bitmap">Pointer to the bitmap.</param>
        public void FreeBitmap(IntPtr bitmap)
        {
            if (bitmap != IntPtr.Zero)
            {
                NativeMtmdMethods.BitmapFree(bitmap);
            }
        }

        /// <summary>
        /// Initializes an empty collection of input chunks.
        /// </summary>
        /// <returns>Pointer to the chunk collection.</returns>
        public IntPtr CreateInputChunks()
        {
            IntPtr chunks = NativeMtmdMethods.InputChunksInit();
            if (chunks == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to initialize input chunks.");
            }

            return chunks;
        }

        /// <summary>
        /// Frees the allocated input chunks collection.
        /// </summary>
        /// <param name="chunks">Pointer to the chunk collection.</param>
        public void FreeInputChunks(IntPtr chunks)
        {
            if (chunks != IntPtr.Zero)
            {
                NativeMtmdMethods.InputChunksFree(chunks);
            }
        }

        /// <summary>
        /// Tokenizes the input text and associates it with the provided bitmaps.
        /// </summary>
        /// <param name="mtmdContext">Pointer to the multimodal context.</param>
        /// <param name="outputChunks">Pointer to the output chunk collection.</param>
        /// <param name="text">Input text containing media markers.</param>
        /// <param name="bitmaps">Array of pointers to native bitmaps.</param>
        /// <returns>Status code of the tokenization process.</returns>
        public int Tokenize(IntPtr mtmdContext, IntPtr outputChunks, string text, IntPtr[] bitmaps)
        {
            if (mtmdContext == IntPtr.Zero) throw new ArgumentException("Context cannot be null.", nameof(mtmdContext));
            if (outputChunks == IntPtr.Zero) throw new ArgumentException("Output chunks cannot be null.", nameof(outputChunks));
            if (text == null) throw new ArgumentNullException(nameof(text));

            IntPtr pText = Marshal.StringToHGlobalAnsi(text);
            try
            {
                var input = new NativeMtmdMethods.MtmdInputText
                {
                    Text = pText,
                    AddSpecial = true,
                    ParseSpecial = true
                };

                return NativeMtmdMethods.Tokenize(mtmdContext, outputChunks, ref input, bitmaps, (nuint)(bitmaps?.Length ?? 0));
            }
            finally
            {
                Marshal.FreeHGlobal(pText);
            }
        }

        /// <summary>
        /// Encodes a single chunk through the multimodal model.
        /// </summary>
        /// <param name="mtmdContext">Pointer to the multimodal context.</param>
        /// <param name="chunk">Pointer to the chunk to encode.</param>
        /// <returns>Status code of the encoding process.</returns>
        public int EncodeChunk(IntPtr mtmdContext, IntPtr chunk)
        {
            if (mtmdContext == IntPtr.Zero) throw new ArgumentException("Context cannot be null.", nameof(mtmdContext));
            if (chunk == IntPtr.Zero) throw new ArgumentException("Chunk cannot be null.", nameof(chunk));

            return NativeMtmdMethods.EncodeChunk(mtmdContext, chunk);
        }

        /// <summary>
        /// Retrieves the computed output embeddings.
        /// </summary>
        /// <param name="mtmdContext">Pointer to the multimodal context.</param>
        /// <param name="nTokens">Number of tokens processed.</param>
        /// <param name="nEmbdDim">Embedding dimension size.</param>
        /// <returns>Array containing the output embeddings.</returns>
        public float[] GetOutputEmbeddings(IntPtr mtmdContext, int nTokens, int nEmbdDim)
        {
            if (mtmdContext == IntPtr.Zero) throw new ArgumentException("Context cannot be null.", nameof(mtmdContext));

            IntPtr ptr = NativeMtmdMethods.GetOutputEmbd(mtmdContext);
            if (ptr == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to retrieve multimodal embeddings. Pointer is null.");
            }

            int totalElements = nTokens * nEmbdDim;
            float[] result = new float[totalElements];

            Marshal.Copy(ptr, result, 0, totalElements);

            return result;
        }

        /// <summary>
        /// Gets the total number of chunks in the collection.
        /// </summary>
        /// <param name="chunks">Pointer to the chunk collection.</param>
        /// <returns>Number of chunks.</returns>
        public int GetChunksCount(IntPtr chunks)
        {
            if (chunks == IntPtr.Zero) throw new ArgumentException("Chunks pointer cannot be null.", nameof(chunks));

            return (int)NativeMtmdMethods.InputChunksSize(chunks);
        }

        /// <summary>
        /// Retrieves a specific chunk from the collection by index.
        /// </summary>
        /// <param name="chunks">Pointer to the chunk collection.</param>
        /// <param name="index">Zero-based index of the chunk.</param>
        /// <returns>Pointer to the specified chunk.</returns>
        public IntPtr GetChunk(IntPtr chunks, int index)
        {
            if (chunks == IntPtr.Zero) throw new ArgumentException("Chunks pointer cannot be null.", nameof(chunks));
            if (index < 0) throw new ArgumentOutOfRangeException(nameof(index), "Index cannot be negative.");

            IntPtr chunk = NativeMtmdMethods.InputChunksGet(chunks, (nuint)index);
            if (chunk == IntPtr.Zero)
            {
                throw new InvalidOperationException($"Failed to retrieve chunk at index {index}.");
            }

            return chunk;
        }

        /// <summary>
        /// Identifies the type of the specified chunk mapped to the domain enum.
        /// </summary>
        /// <param name="chunk">Pointer to the chunk.</param>
        /// <returns>The domain-mapped chunk type.</returns>
        public InputChunkType GetChunkType(IntPtr chunk)
        {
            if (chunk == IntPtr.Zero) throw new ArgumentException("Chunk cannot be null.", nameof(chunk));

            var nativeType = NativeMtmdMethods.InputChunkGetType(chunk);
            return nativeType switch
            {
                NativeMtmdMethods.MtmdInputChunkType.Text => InputChunkType.Text,
                NativeMtmdMethods.MtmdInputChunkType.Image => InputChunkType.Image,
                NativeMtmdMethods.MtmdInputChunkType.Audio => InputChunkType.Audio,
                _ => throw new NotSupportedException($"Chunk type {nativeType} is not supported.")
            };
        }

        /// <summary>
        /// Gets the number of tokens contained within the specified chunk.
        /// </summary>
        /// <param name="chunk">Pointer to the chunk.</param>
        /// <returns>Token count.</returns>
        public int GetChunkTokenCount(IntPtr chunk)
        {
            if (chunk == IntPtr.Zero) throw new ArgumentException("Chunk cannot be null.", nameof(chunk));

            return (int)NativeMtmdMethods.InputChunkGetNTokens(chunk);
        }

        /// <summary>
        /// Extracts the text tokens from a text chunk.
        /// </summary>
        /// <param name="chunk">Pointer to the chunk.</param>
        /// <returns>Array of text tokens.</returns>
        public int[] GetChunkTokens(IntPtr chunk)
        {
            if (chunk == IntPtr.Zero) throw new ArgumentException("Chunk cannot be null.", nameof(chunk));

            if (GetChunkType(chunk) != InputChunkType.Text)
            {
                throw new InvalidOperationException("Cannot retrieve text tokens from a non-text chunk.");
            }

            IntPtr ptr = NativeMtmdMethods.InputChunkGetTokensText(chunk, out nuint tokenCount);
            if (ptr == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to retrieve text tokens from chunk.");
            }

            if (tokenCount == 0)
            {
                return Array.Empty<int>();
            }

            int[] tokens = new int[(int)tokenCount];
            Marshal.Copy(ptr, tokens, 0, (int)tokenCount);
            return tokens;
        }

        /// <summary>
        /// Initializes a multimodal batch for encoding.
        /// </summary>
        /// <param name="mtmdContext">Pointer to the multimodal context.</param>
        /// <returns>Pointer to the initialized batch.</returns>
        public IntPtr BatchInit(IntPtr mtmdContext)
        {
            if (mtmdContext == IntPtr.Zero) throw new ArgumentException("Context cannot be null.", nameof(mtmdContext));

            IntPtr batch = NativeMtmdMethods.BatchInit(mtmdContext);
            if (batch == IntPtr.Zero) throw new InvalidOperationException("Failed to initialize MTMD batch.");

            return batch;
        }

        /// <summary>
        /// Frees the allocated multimodal batch.
        /// </summary>
        /// <param name="batch">Pointer to the batch.</param>
        public void BatchFree(IntPtr batch)
        {
            if (batch != IntPtr.Zero)
            {
                NativeMtmdMethods.BatchFree(batch);
            }
        }

        /// <summary>
        /// Adds a chunk to the multimodal batch.
        /// </summary>
        /// <param name="batch">Pointer to the batch.</param>
        /// <param name="chunk">Pointer to the chunk.</param>
        public void BatchAddChunk(IntPtr batch, IntPtr chunk)
        {
            if (batch == IntPtr.Zero) throw new ArgumentException("Batch cannot be null.", nameof(batch));
            if (chunk == IntPtr.Zero) throw new ArgumentException("Chunk cannot be null.", nameof(chunk));

            NativeMtmdMethods.BatchAddChunk(batch, chunk);
        }

        /// <summary>
        /// Encodes the multimodal batch.
        /// </summary>
        /// <param name="batch">Pointer to the batch.</param>
        /// <returns>Status code of the encoding process.</returns>
        public int BatchEncode(IntPtr batch)
        {
            if (batch == IntPtr.Zero) throw new ArgumentException("Batch cannot be null.", nameof(batch));

            return NativeMtmdMethods.BatchEncode(batch);
        }
    }
}