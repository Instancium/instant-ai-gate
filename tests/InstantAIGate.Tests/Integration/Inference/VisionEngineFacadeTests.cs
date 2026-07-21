using InstantAIGate.Infrastructure.Inference.layers;
using InstantAIGate.Infrastructure.Inference.Native;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Xunit;

namespace InstantAIGate.Tests.Integration.Inference
{
    /// <summary>
    /// Integration tests for VisionEngineFacade.
    /// Requires native libmtmd binaries and a valid tiny multimodal model in the output directory.
    /// </summary>
    public sealed class VisionEngineFacadeTests : IDisposable
    {
        private readonly VisionEngineFacade _facade;
        private VisionContext? _visionContext;
        private NativeLlamaApi? _llamaApi;
        private IntPtr _textModelHandle = IntPtr.Zero;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetDllDirectory(string lpPathName);

        public VisionEngineFacadeTests()
        {
            var logger = NullLogger<VisionEngineFacade>.Instance;
            _facade = new VisionEngineFacade(logger);

            string rootPath = GetSolutionRootDirectory();
            string windowsRuntimePath = Path.Combine(rootPath, ".runtimes", "Windows", "x64");
            SetDllDirectory(windowsRuntimePath);
        }

        private static string GetSolutionRootDirectory()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !directory.GetFiles("InstantAIGate.sln").Any() && !directory.GetFiles("InstantAIGate.slnx").Any())
            {
                directory = directory.Parent;
            }

            return directory?.FullName ?? throw new DirectoryNotFoundException("Could not find the solution root directory.");
        }

        [Fact]
        public void PrepareMediaValidated_EmptyByteArray_ThrowsArgumentException()
        {
            // Arrange
            byte[] emptyData = Array.Empty<byte>();
            uint width = 800;
            uint height = 600;
            string hashId = "test-hash-123";

            // Act & Assert
            Assert.Throws<ArgumentException>(() =>
                _facade.PrepareMediaValidated(emptyData, width, height, hashId));
        }

        [Fact]
        public void InitializeVision_InvalidProjectorPath_ThrowsArgumentException()
        {
            // Arrange
            string emptyPath = string.Empty;
            IntPtr dummyHandle = new IntPtr(1);

            // Act & Assert
            Assert.Throws<ArgumentException>(() =>
                _facade.InitializeVision(emptyPath, dummyHandle));
        }

        [Fact]
        public void ProcessMedia_ValidInputs_ReturnsExtractedVisionData()
        {
            // Arrange
            string rootPath = GetSolutionRootDirectory();

            string textModelPath = Path.Combine(rootPath, "storage", "models", "Qwen_Qwen3-VL-2B-Instruct-GGUF", "Qwen3VL-2B-Instruct-Q4_K_M.gguf");
            string projectorPath = Path.Combine(rootPath, "storage", "models", "Qwen_Qwen3-VL-2B-Instruct-GGUF", "nmmproj-Qwen3VL-2B-Instruct-Q8_0.gguf");

            if (!File.Exists(textModelPath) || !File.Exists(projectorPath))
            {
                throw new FileNotFoundException("Qwen3-VL model files not found in the storage directory. Ensure they are downloaded.");
            }

            var llamaLogger = NullLogger<NativeLlamaApi>.Instance;
            _llamaApi = new NativeLlamaApi();
            _llamaApi.LoadAllBackends();
            _llamaApi.BackendInit();

            _textModelHandle = _llamaApi.LoadModel(
                path: textModelPath,
                gpuLayers: 0,
                mainGpu: 0,
                useMlock: false,
                useMmap: true,
                splitMode: NativeLlamaSplitMode.None);

            Assert.NotEqual(IntPtr.Zero, _textModelHandle);

            byte[] dummyRgbData = new byte[256 * 256 * 3];
            uint width = 256;
            uint height = 256;
            string hashId = "image-sha256-hash";
            string prompt = "Describe this image: <__media__>";

            _visionContext = _facade.InitializeVision(projectorPath, _textModelHandle, useGpu: false, batchMaxTokens: 4096);

            IntPtr bitmapPtr = IntPtr.Zero;
            IntPtr rawRgbPtr = IntPtr.Zero;
            IntPtr chunksPtr = IntPtr.Zero;
            IntPtr batchPtr = IntPtr.Zero;

            try
            {
                // Act
                (bitmapPtr, rawRgbPtr) = _facade.PrepareMediaValidated(dummyRgbData, width, height, hashId);

                var (newChunksPtr, visionChunkPtr) = _facade.TokenizeAndValidateChunks(_visionContext.Handle, prompt, bitmapPtr);
                chunksPtr = newChunksPtr;

                batchPtr = _facade.EncodeBatchSafe(_visionContext.Handle, chunksPtr);

                ExtractedVisionData result = _facade.ExtractInferenceData(_visionContext.Handle, batchPtr, visionChunkPtr);

                // Assert
                Assert.NotNull(result);
                Assert.NotEqual(IntPtr.Zero, result.EmbeddingsPtr);
                Assert.True(result.TokenCount > 0);
                Assert.NotEmpty(result.Positions);

                // M-RoPE Architectural Assertions
                Assert.True(result.Positions.Length > 0, "M-RoPE models must extract spatial positional data.");
                Assert.True(result.TokenCount >= result.Positions.Length, "In M-RoPE, token count must be greater than or equal to the number of positional blocks.");

                // Qwen3-VL utilizes M-RoPE for spatial awareness, allowing the LLM decoder to maintain standard causal attention.
                // Therefore, RequiresNonCausalAttention evaluates to false for this specific model architecture.
                Assert.False(result.RequiresNonCausalAttention, "Qwen3-VL uses M-RoPE and does not require non-causal attention in the decoder.");
            }
            finally
            {
                if (batchPtr != IntPtr.Zero) NativeMtmdMethods.BatchFree(batchPtr);
                if (chunksPtr != IntPtr.Zero) NativeMtmdMethods.InputChunksFree(chunksPtr);
                if (bitmapPtr != IntPtr.Zero) NativeMtmdMethods.BitmapFree(bitmapPtr);
                if (rawRgbPtr != IntPtr.Zero) Marshal.FreeHGlobal(rawRgbPtr);
            }
        }

        public void Dispose()
        {
            _visionContext?.Dispose();

            if (_llamaApi != null && _textModelHandle != IntPtr.Zero)
            {
                _llamaApi.FreeModel(_textModelHandle);
                _llamaApi.BackendFree();
            }

            SetDllDirectory(string.Empty);
        }
    }
}