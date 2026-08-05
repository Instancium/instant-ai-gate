using InstantAIGate.Infrastructure.Inference.layers;
using InstantAIGate.Infrastructure.Inference.Native;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace InstantAIGate.Infrastructure.Inference.Facades
{
    public interface IVisionBitmap : IDisposable
    {
        uint Width { get; }
        uint Height { get; }
        void SetTrackingId(string id);
    }

    public interface IVisionFacade : IDisposable
    {
        VisionContext InitializeContext(string mmprojPath, IntPtr textModelPtr);
        IVisionBitmap LoadImageFromFile(string filePath);
        IVisionBitmap LoadImageFromMemory(byte[] fileBytes);
        IVisionBitmap CreateFromRawRgb(byte[] rawRgbData, uint width, uint height);
        float[] ProcessAndEncodePrompt(string prompt, IEnumerable<IVisionBitmap> images);
    }

    public sealed class VisionBitmap : IVisionBitmap
    {
        private readonly IntPtr _bitmapHandle;
        private bool _disposed;

        public uint Width { get; }
        public uint Height { get; }

        internal IntPtr Handle => _disposed ? throw new ObjectDisposedException(nameof(VisionBitmap)) : _bitmapHandle;

        /// <summary> Stage 2: Load image from file using mtmd resources without third-party libraries. </summary>
        internal VisionBitmap(IntPtr contextHandle, string filePath)
        {
            if (contextHandle == IntPtr.Zero)
                throw new ArgumentException("Context handle is required.", nameof(contextHandle));

            NativeMtmdMethods.MtmdHelperBitmapWrapper wrapper = NativeMtmdMethods.HelperBitmapInitFromFile(
                contextHandle,
                filePath,
                false);

            _bitmapHandle = wrapper.Bitmap;
            if (_bitmapHandle == IntPtr.Zero)
            {
                throw new InvalidOperationException($"Failed to load and decode image from file: {filePath}");
            }

            Width = NativeMtmdMethods.BitmapGetNx(_bitmapHandle);
            Height = NativeMtmdMethods.BitmapGetNy(_bitmapHandle);
        }

        /// <summary> Stage 2: Load image from byte buffer (JPG/PNG bytes) using mtmd resources. </summary>
        internal VisionBitmap(IntPtr contextHandle, byte[] fileBytes)
        {
            if (contextHandle == IntPtr.Zero)
                throw new ArgumentException("Context handle is required.", nameof(contextHandle));

            unsafe
            {
                fixed (byte* pBuffer = fileBytes)
                {
                    NativeMtmdMethods.MtmdHelperBitmapWrapper wrapper = NativeMtmdMethods.HelperBitmapInitFromBuf(
                        contextHandle,
                        (IntPtr)pBuffer,
                        (nuint)fileBytes.Length,
                        false);

                    _bitmapHandle = wrapper.Bitmap;
                }
            }

            if (_bitmapHandle == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to decode image from buffer.");
            }

            Width = NativeMtmdMethods.BitmapGetNx(_bitmapHandle);
            Height = NativeMtmdMethods.BitmapGetNy(_bitmapHandle);
        }

        /// <summary> Alternate Stage 2: Create directly from raw RGB pixels. </summary>
        internal VisionBitmap(byte[] rawRgbData, uint width, uint height)
        {
            unsafe
            {
                fixed (byte* pData = rawRgbData)
                {
                    _bitmapHandle = NativeMtmdMethods.BitmapInit(width, height, (IntPtr)pData);
                }
            }

            if (_bitmapHandle == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to allocate mtmd_bitmap from raw RGB.");
            }

            Width = NativeMtmdMethods.BitmapGetNx(_bitmapHandle);
            Height = NativeMtmdMethods.BitmapGetNy(_bitmapHandle);
        }

        public void SetTrackingId(string id)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(VisionBitmap));
            NativeMtmdMethods.BitmapSetId(_bitmapHandle, id);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                if (_bitmapHandle != IntPtr.Zero)
                {
                    NativeMtmdMethods.BitmapFree(_bitmapHandle);
                }
                _disposed = true;
            }
        }
    }

    public sealed class VisionEngineFacade : IVisionFacade
    {
        private IntPtr _contextHandle = IntPtr.Zero;
        private bool _disposed;

        /// <summary> Stage 1: Multimodal context initialization (Vision Setup). </summary>
        public VisionContext InitializeContext(string mmprojPath, IntPtr textModelPtr)
        {
            EnsureNotDisposed();

            if (_contextHandle != IntPtr.Zero)
            {
                throw new InvalidOperationException("Context is already initialized.");
            }

            NativeMtmdMethods.MtmdContextParams ctxParams = NativeMtmdMethods.GetDefaultContextParams();
            ctxParams.UseGpu = true;

            _contextHandle = NativeMtmdMethods.InitFromFile(mmprojPath, textModelPtr, ctxParams);

            if (_contextHandle == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to initialize mtmd multimodal context.");
            }

            return new  VisionContext(_contextHandle);
        }

        /// <summary> Stage 2: Wrapper for file loading. </summary>
        public IVisionBitmap LoadImageFromFile(string filePath)
        {
            EnsureNotDisposed();
            EnsureContextInitialized();
            return new VisionBitmap(_contextHandle, filePath);
        }

        /// <summary> Stage 2: Wrapper for memory loading. </summary>
        public IVisionBitmap LoadImageFromMemory(byte[] fileBytes)
        {
            EnsureNotDisposed();
            EnsureContextInitialized();
            return new VisionBitmap(_contextHandle, fileBytes);
        }

        /// <summary> Stage 2: Wrapper for raw RGB creation. </summary>
        public IVisionBitmap CreateFromRawRgb(byte[] rawRgbData, uint width, uint height)
        {
            EnsureNotDisposed();
            return new VisionBitmap(rawRgbData, width, height);
        }

        /// <summary> Stages 3, 4, 5: Tokenization, Batch Encoding, and Embedding Extraction. </summary>
        public float[] ProcessAndEncodePrompt(string prompt, IEnumerable<IVisionBitmap> images)
        {
            EnsureNotDisposed();
            EnsureContextInitialized();

            List<IntPtr> bitmapHandles = new List<IntPtr>();
            foreach (var img in images)
            {
                if (img is VisionBitmap visionBitmap)
                {
                    bitmapHandles.Add(visionBitmap.Handle);
                }
                else
                {
                    throw new ArgumentException("Unsupported IVisionBitmap implementation.", nameof(images));
                }
            }

            // Stage 3: Prompt tokenization and chunking
            IntPtr inputChunks = NativeMtmdMethods.InputChunksInit();
            try
            {
                IntPtr promptPtr = Marshal.StringToHGlobalAnsi(prompt);
                try
                {
                    NativeMtmdMethods.MtmdInputText inputText = new NativeMtmdMethods.MtmdInputText
                    {
                        Text = promptPtr,
                        AddSpecial = true,
                        ParseSpecial = true
                    };

                    int tokenizationResult = NativeMtmdMethods.Tokenize(
                        _contextHandle,
                        inputChunks,
                        ref inputText,
                        bitmapHandles.ToArray(),
                        (nuint)bitmapHandles.Count);

                    if (tokenizationResult != 0)
                    {
                        throw new InvalidOperationException($"Tokenization failed with code: {tokenizationResult}");
                    }

                    // Stages 4 and 5: Batch encoding and embedding extraction
                    return EncodeAndExtractEmbeddings(inputChunks);
                }
                finally
                {
                    Marshal.FreeHGlobal(promptPtr);
                }
            }
            finally
            {
                NativeMtmdMethods.InputChunksFree(inputChunks);
            }
        }

        private float[] EncodeAndExtractEmbeddings(IntPtr inputChunks)
        {
            IntPtr batchHandle = NativeMtmdMethods.BatchInit(_contextHandle);
            try
            {
                nuint chunkCount = NativeMtmdMethods.InputChunksSize(inputChunks);
                IntPtr lastImageChunk = IntPtr.Zero;

                for (nuint i = 0; i < chunkCount; i++)
                {
                    IntPtr chunk = NativeMtmdMethods.InputChunksGet(inputChunks, i);
                    NativeMtmdMethods.BatchAddChunk(batchHandle, chunk);

                    if (NativeMtmdMethods.InputChunkGetType(chunk) == NativeMtmdMethods.MtmdInputChunkType.Image)
                    {
                        lastImageChunk = chunk;
                    }
                }

                // Stage 4: Batch Encoding
                int encodeResult = NativeMtmdMethods.BatchEncode(batchHandle);
                if (encodeResult != 0)
                {
                    throw new InvalidOperationException($"Batch encoding failed with code: {encodeResult}");
                }

                // Stage 5: Vector extraction
                if (lastImageChunk != IntPtr.Zero)
                {
                    IntPtr embeddingsPtr = NativeMtmdMethods.BatchGetOutputEmbd(batchHandle, lastImageChunk);

                    if (embeddingsPtr != IntPtr.Zero)
                    {
                        nuint tokensCount = NativeMtmdMethods.InputChunkGetNTokens(lastImageChunk);

                        // Embedding dimensionality depends on the projector architecture
                        int vectorSize = 4096;
                        int totalFloats = (int)tokensCount * vectorSize;

                        float[] result = new float[totalFloats];
                        Marshal.Copy(embeddingsPtr, result, 0, totalFloats);
                        return result;
                    }
                }

                return Array.Empty<float>();
            }
            finally
            {
                NativeMtmdMethods.BatchFree(batchHandle);
            }
        }

        private void EnsureContextInitialized()
        {
            if (_contextHandle == IntPtr.Zero)
            {
                throw new InvalidOperationException("Context is not initialized. Call InitializeContext first.");
            }
        }

        private void EnsureNotDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(VisionEngineFacade));
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                if (_contextHandle != IntPtr.Zero)
                {
                    NativeMtmdMethods.Free(_contextHandle);
                    _contextHandle = IntPtr.Zero;
                }
                _disposed = true;
            }
        }
    }
}