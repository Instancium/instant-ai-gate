using InstantAIGate.Infrastructure.Inference;
using InstantAIGate.Infrastructure.Inference.Adapters;
using InstantAIGate.Infrastructure.Inference.layers;
using InstantAIGate.Infrastructure.Inference.Native;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestPlatform.Utilities;
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace InstantAIGate.Tests.Integration.Inference
{
    public sealed class VisionPipelineIntegrationTests : IDisposable
    {
        private readonly VisionEngineFacade _visionFacade;
        private readonly LlamaEngineFacade _llamaFacade;
        private readonly VisionAdapter _adapter;
        private readonly ITestOutputHelper _output;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetDllDirectory(string lpPathName);

        public VisionPipelineIntegrationTests(ITestOutputHelper output) 
        {
            _output = output;
            _visionFacade = new VisionEngineFacade(NullLogger<VisionEngineFacade>.Instance);
            _llamaFacade = new LlamaEngineFacade(NullLogger<LlamaEngineFacade>.Instance);
            _adapter = new VisionAdapter(_visionFacade, _llamaFacade, NullLogger<VisionAdapter>.Instance);

            string rootPath = GetSolutionRootDirectory();
            string windowsRuntimePath = Path.Combine(rootPath, ".runtimes", "Windows", "x64");
            SetDllDirectory(windowsRuntimePath);

            NativeLlamaMethods.LlamaBackendInit();
        }

        private static string GetSolutionRootDirectory()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !directory.GetFiles("InstantAIGate.sln").Any() && !directory.GetFiles("InstantAIGate.slnx").Any())
            {
                directory = directory.Parent;
            }
            return directory?.FullName ?? throw new DirectoryNotFoundException("Could not find solution root.");
        }



        [Fact]
        public async Task VisionAdapter_GenerateResponse_ReturnsValidText()
        {
            // Arrange
            string rootPath = GetSolutionRootDirectory();
            string textModelPath = Path.Combine(rootPath, "storage", "models", "Qwen_Qwen3-VL-2B-Instruct-GGUF", "Qwen3VL-2B-Instruct-Q4_K_M.gguf");
            string projectorPath = Path.Combine(rootPath, "storage", "models", "Qwen_Qwen3-VL-2B-Instruct-GGUF", "nmmproj-Qwen3VL-2B-Instruct-Q8_0.gguf");

            if (!File.Exists(textModelPath) || !File.Exists(projectorPath))
            {
                throw new FileNotFoundException("Qwen3-VL E2E test files missing.");
            }

            var modelParams = NativeLlamaMethods.LlamaModelDefaultParams();
            modelParams.NGpuLayers = 0;
            modelParams.UseMmap = true;
            IntPtr textModelHandle = NativeLlamaMethods.LlamaModelLoadFromFile(textModelPath, modelParams);
            Assert.NotEqual(IntPtr.Zero, textModelHandle);

            var ctxParams = NativeLlamaMethods.LlamaContextDefaultParams();
            ctxParams.NCtx = 4096;
            ctxParams.NBatch = 2048;
            ctxParams.NUBatch = 256;
            ctxParams.Embeddings = false;

            IntPtr llamaContext = NativeLlamaMethods.LlamaInitFromModel(textModelHandle, ctxParams);
            Assert.NotEqual(IntPtr.Zero, llamaContext);

            IntPtr vocab = NativeLlamaMethods.LlamaModelGetVocab(textModelHandle);

            // Инициализация уже содержит встроенный аппаратный прогрев (Warmup = true)
            using var visionContext = _visionFacade.InitializeVision(projectorPath, textModelHandle, useGpu: false, batchMaxTokens: 4096);

            byte[] imageBytes = new byte[256 * 256 * 3];
            string prompt = "<|im_start|>system\nYou are a helpful assistant.<|im_end|>\n<|im_start|>user\n<__media__>\nWhat color is the image?<|im_end|>\n<|im_start|>assistant\n";

            var outputBuilder = new StringBuilder();

            try
            {
                _output.WriteLine("=== STARTING MODEL GENERATION ===");

                await foreach (var piece in _adapter.StreamVisionResponseAsync(
                    visionContext,
                    llamaContext,
                    vocab,
                    prompt,
                    imageBytes,
                    256,
                    256,
                    "test-image-1",
                    maxTokens: 15))
                {
                    outputBuilder.Append(piece);
                }

                string finalResponse = outputBuilder.ToString().Trim();
                _output.WriteLine(finalResponse);
                _output.WriteLine(Environment.NewLine + $"=== GENERATION COMPLETED. Final length: {finalResponse.Length} ===");

                Assert.NotNull(finalResponse);
            }
            finally
            {
                NativeLlamaMethods.LlamaFree(llamaContext);
                NativeLlamaMethods.LlamaModelFree(textModelHandle);
            }
        }

        public void Dispose()
        {
            NativeLlamaMethods.LlamaBackendFree();
            SetDllDirectory(string.Empty);
        }
    }
}