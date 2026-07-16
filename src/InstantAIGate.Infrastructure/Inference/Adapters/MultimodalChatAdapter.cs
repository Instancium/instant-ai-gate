using InstantAIGate.Application.Dtos.Requests;
using InstantAIGate.Application.Interfaces.Inference;
using InstantAIGate.Application.Interfaces.Storage;
using InstantAIGate.Infrastructure.Inference.Native;
using InstantAIGate.Infrastructure.Templates;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

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

        public MultimodalChatAdapter(
            ILlamaModelManager llamaManager,
            IMtmdClipModelManager mtmdManager,
            IImageContentResolver imageResolver,
            INativeMtmdApi mtmdApi,
            INativeLlamaApi llamaApi,
            IModelPathProvider pathProvider,
            ILogger<MultimodalChatAdapter> logger)
        {
            _llamaManager = llamaManager;
            _mtmdManager = mtmdManager;
            _imageResolver = imageResolver;
            _mtmdApi = mtmdApi;
            _llamaApi = llamaApi;
            _pathProvider = pathProvider;
            _logger = logger;
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
            // 1. Extract the first image URL from messages (currently supporting one image)
            string? imageUrl = ExtractFirstImageUrl(request.Messages);
            if (string.IsNullOrEmpty(imageUrl))
                throw new InvalidOperationException("No image found in the multimodal request.");

            // 2. Download and decode the image into a flat RGB array
            var imageResult = await _imageResolver.ResolveAsync(imageUrl, ct);

            // 3. Retrieve text model configuration
            string textModelPath = await _pathProvider.GetFullModelPathAsync(request.Model);
            var profile = ModelProfileResolver.Resolve(textModelPath);

            // TODO: Projector path should ideally be read from the model configuration registry
            string projectorPath = textModelPath.Replace(".gguf", "-mmproj.gguf");

            // 4. Acquire contexts sequentially to satisfy native pointer dependencies
            using var llamaModel = await _llamaManager.AcquireModelAsync(request.Model, ct);
            using var llamaContext = await _llamaManager.AcquireContextAsync(request.Model, ct);

            using var mtmdContext = await _mtmdManager.AcquireContextAsync(
                projectorPath,
                llamaModel.Handle,
                useGpu: true,
                ct);

            // ==========================================
            // NATIVE MEMORY ALLOCATION PHASE
            // ==========================================

            // Pin the RGB array to prevent Garbage Collector from moving it during unmanaged execution
            GCHandle pinnedRgb = GCHandle.Alloc(imageResult.RgbData, GCHandleType.Pinned);

            IntPtr nativeBitmap = IntPtr.Zero;
            IntPtr nativeChunks = IntPtr.Zero;
            IntPtr[] bitmapArray = new IntPtr[1];

            try
            {
                // Initialize native bitmap from raw bytes
                nativeBitmap = _mtmdApi.CreateBitmap(imageResult.Width, imageResult.Height, pinnedRgb.AddrOfPinnedObject());
                bitmapArray[0] = nativeBitmap;

                // Allocate chunk container
                nativeChunks = _mtmdApi.CreateInputChunks();

                // Build standard text prompt from message history
                string prompt = profile.Template.BuildPrompt(request.Messages);

                // Tokenize text and merge with the image bitmap into input chunks
                int tokensCount = _mtmdApi.Tokenize(mtmdContext.Handle, nativeChunks, prompt, bitmapArray);

                if (tokensCount <= 0)
                    throw new InvalidOperationException("Failed to tokenize multimodal prompt.");

                _logger.LogInformation("Multimodal prompt tokenized into {Count} tokens/chunks.", tokensCount);

                // ==========================================
                // INFERENCE PHASE (TODO)
                // ==========================================

                yield return "[VISION PIPELINE EXPERIMENTAL RESPONSE]";
            }
            finally
            {
                // Ensure strict cleanup of all native allocations and unpin managed arrays
                if (nativeChunks != IntPtr.Zero) _mtmdApi.FreeInputChunks(nativeChunks);
                if (nativeBitmap != IntPtr.Zero) _mtmdApi.FreeBitmap(nativeBitmap);

                if (pinnedRgb.IsAllocated) pinnedRgb.Free();
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