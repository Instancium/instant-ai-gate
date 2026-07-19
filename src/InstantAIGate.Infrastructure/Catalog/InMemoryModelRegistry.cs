
using InstantAIGate.Application.Interfaces.Catalog;
using InstantAIGate.Domain.Entities;
using InstantAIGate.Domain.Enums;


namespace InstantAIGate.Infrastructure.Catalog
{

    public class InMemoryModelRegistry : IModelRegistry
    {
        private static readonly List<ModelManifest> _models =
        [
            new(
                "Qwen/Qwen3-VL-2B-Instruct-GGUF",
                "Qwen 3 VL 2B Instruct (Q4_K_M)",
                files: new List<ModelFile> {
                    new("Qwen3VL-2B-Instruct-Q4_K_M.gguf",
                        "https://modelscope.ai/models/Qwen/Qwen3-VL-2B-Instruct-GGUF/resolve/master/Qwen3VL-2B-Instruct-Q4_K_M.gguf",
                        1_110_000_000L),
                    new("nmmproj-Qwen3VL-2B-Instruct-Q8_0.gguf",
                        "https://modelscope.ai/models/Qwen/Qwen3-VL-2B-Instruct-GGUF/resolve/master/mmproj-Qwen3VL-2B-Instruct-Q8_0.gguf",
                        445_000_000)
                }
            ),
            new(
                "Qwen/Qwen3-VL-4B-Instruct-GGUF",
                "Qwen 3 VL 4B Instruct (Q4_K_M)",
                files: new List<ModelFile> {
                    new("Qwen3VL-4B-Instruct-Q4_K_M.gguf",
                        "https://modelscope.ai/models/Qwen/Qwen3-VL-4B-Instruct-GGUF/resolve/master/Qwen3VL-4B-Instruct-Q4_K_M.gguf",
                        2_500_000_000L)
                }
            ),
            new(
                "Qwen/Qwen3-VL-8B-Instruct-GGUF",
                "Qwen 3 VL 8B Instruct (Q4_K_M)",
                files: new List<ModelFile> { new("Qwen3VL-8B-Instruct-Q4_K_M.gguf", "https://modelscope.ai/models/Qwen/Qwen3-VL-8B-Instruct-GGUF/resolve/master/Qwen3VL-8B-Instruct-Q4_K_M.gguf", 5_030_000_000L) }
            ),
            new(
                "Qwen/Qwen3-VL-32B-Instruct-GGUF",
                "Qwen 3 VL 32B Instruct (Q4_K_M)",
                files: new List<ModelFile> { new("Qwen3VL-32B-Instruct-Q4_K_M.gguf", "https://modelscope.ai/models/Qwen/Qwen3-VL-32B-Instruct-GGUF/resolve/master/Qwen3VL-32B-Instruct-Q4_K_M.gguff", 19_760_000_000L) }
            ),
            new(
                "ggml-org/bge-m3-Q8_0-GGUF",
                "BGE-M3 Multilingual Embedding (Q8_0)",
                files: new List<ModelFile> { new("bge-m3-q8_0.gguf", "https://huggingface.co/ggml-org/bge-m3-Q8_0-GGUF/resolve/main/bge-m3-q8_0.gguf", 634_553_760L) },
                type: ModelType.Bert
            )
        ];

        public Task<IReadOnlyList<ModelManifest>> GetAllModelsAsync()
        {
            return Task.FromResult<IReadOnlyList<ModelManifest>>(_models);
        }

        public Task<ModelManifest?> GetModelAsync(string repoId)
        {
            var model = _models.FirstOrDefault(m => m.RepoId == repoId);
            return Task.FromResult(model);
        }

    }

}

