using System;

namespace InstantAIGate.Infrastructure.Inference.Native
{
    /// <summary>
    /// Default implementation of <see cref="IMtmdClipApi"/> routing to MTMD P/Invoke methods.
    /// </summary>
    public sealed class MtmdClipApi : IMtmdClipApi
    {
        public NativeMethods.clip_cap GetCapabilities(string filePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            return NativeMethods.clip_get_cap(filePath);
        }

        public NativeMethods.clip_init_result Initialize(string filePath, bool useGpu)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

            var @params = new NativeMethods.clip_context_params
            {
                use_gpu = useGpu,
                flash_attn_type = NativeMethods.clip_flash_attn_type.CLIP_FLASH_ATTN_TYPE_AUTO,
                image_min_tokens = 0,
                image_max_tokens = 0,
                warmup = false,
                cb_eval = IntPtr.Zero,
                cb_eval_user_data = IntPtr.Zero,
                no_alloc = false
            };

            return NativeMethods.clip_init(filePath, @params);
        }

        public void FreeContext(IntPtr context)
        {
            if (context != IntPtr.Zero)
            {
                NativeMethods.clip_free(context);
            }
        }

        public int GetProjectorEmbeddingSize(IntPtr context)
        {
            if (context == IntPtr.Zero)
            {
                throw new ArgumentException("Context pointer cannot be null.", nameof(context));
            }

            return NativeMethods.clip_n_mmproj_embd(context);
        }

        public IntPtr InitializeImageF32()
        {
            return NativeMethods.clip_image_f32_init();
        }

        public void FreeImageF32(IntPtr imageContext)
        {
            if (imageContext != IntPtr.Zero)
            {
                NativeMethods.clip_image_f32_free(imageContext);
            }
        }
    }
}