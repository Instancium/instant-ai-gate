using System;
using System.Runtime.InteropServices;

namespace InstantAIGate.Infrastructure.Inference.Native
{
    public class MtmdApi : INativeMtmdApi
    {
        public NativeMethods.mtmd_caps GetCapabilities(string projectorPath)
        {
            return NativeMethods.mtmd_get_cap_from_file(projectorPath);
        }

        public IntPtr InitializeContext(string projectorPath, IntPtr textModelPtr, bool useGpu)
        {
            var p = NativeMethods.mtmd_context_params_default();
            p.use_gpu = useGpu;
            p.n_threads = Environment.ProcessorCount;

            return NativeMethods.mtmd_init_from_file(projectorPath, textModelPtr, p);
        }

        public void FreeContext(IntPtr mtmdContext)
        {
            if (mtmdContext != IntPtr.Zero)
            {
                NativeMethods.mtmd_free(mtmdContext);
            }
        }

        public IntPtr CreateBitmap(uint width, uint height, IntPtr rgbData)
        {
            return NativeMethods.mtmd_bitmap_init(width, height, rgbData);
        }

        public void FreeBitmap(IntPtr bitmap)
        {
            if (bitmap != IntPtr.Zero)
            {
                NativeMethods.mtmd_bitmap_free(bitmap);
            }
        }

        public IntPtr CreateInputChunks()
        {
            return NativeMethods.mtmd_input_chunks_init();
        }

        public void FreeInputChunks(IntPtr chunks)
        {
            if (chunks != IntPtr.Zero)
            {
                NativeMethods.mtmd_input_chunks_free(chunks);
            }
        }

        public int Tokenize(IntPtr mtmdContext, IntPtr outputChunks, string text, IntPtr[] bitmaps)
        {
            IntPtr pText = Marshal.StringToHGlobalAnsi(text);
            try
            {
                var input = new NativeMethods.mtmd_input_text
                {
                    text = pText,
                    add_special = true,
                    parse_special = true
                };

                return NativeMethods.mtmd_tokenize(mtmdContext, outputChunks, ref input, bitmaps, (nuint)bitmaps.Length);
            }
            finally
            {
                Marshal.FreeHGlobal(pText);
            }
        }

        public int EncodeChunk(IntPtr mtmdContext, IntPtr chunk)
        {
            return NativeMethods.mtmd_encode_chunk(mtmdContext, chunk);
        }

        public float[] GetOutputEmbeddings(IntPtr mtmdContext, int nTokens, int nEmbdDim)
        {
            IntPtr ptr = NativeMethods.mtmd_get_output_embd(mtmdContext);
            if (ptr == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to retrieve multimodal embeddings.");
            }

            int totalElements = nTokens * nEmbdDim;
            float[] result = new float[totalElements];

            // Safe, fast bulk copy from unmanaged pointer to C# array
            Marshal.Copy(ptr, result, 0, totalElements);

            return result;
        }

        public int GetChunksCount(IntPtr chunks)
        {
            return (int)NativeMethods.mtmd_input_chunks_get_size(chunks);
        }

        public IntPtr GetChunk(IntPtr chunks, int index)
        {
            return NativeMethods.mtmd_input_chunks_get_chunk(chunks, (nuint)index);
        }

        public NativeMethods.mtmd_input_chunk_type GetChunkType(IntPtr chunk)
        {
            return NativeMethods.mtmd_input_chunk_get_type(chunk);
        }

        public int GetChunkTokenCount(IntPtr chunk)
        {
            return (int)NativeMethods.mtmd_input_chunk_get_n_tokens(chunk);
        }

        public int[] GetChunkTokens(IntPtr chunk, int tokenCount)
        {
            if (tokenCount <= 0) return Array.Empty<int>();

            IntPtr ptr = NativeMethods.mtmd_input_chunk_get_tokens(chunk);
            if (ptr == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to retrieve text tokens from chunk.");
            }

            int[] tokens = new int[tokenCount];
            Marshal.Copy(ptr, tokens, 0, tokenCount);
            return tokens;
        }
    }
}