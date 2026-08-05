using InstantAIGate.Application.ModelManagement.Conteracts;
using InstantAIGate.Domain.Entities;
using InstantAIGate.Domain.Enums;
using System.Collections.Generic;

namespace InstantAIGate.Infrastructure.ModelManagement
{
    public class SupportedModelsDictionary : IModelCatalog
    {
        private static readonly IReadOnlyDictionary<string, SupportedModelDefinition> Models = new Dictionary<string, SupportedModelDefinition>
        {
            {
                "Qwen/Qwen3-VL-4B-Instruct-ONNX",
                new SupportedModelDefinition
                {
                    RepoId = "Qwen/Qwen3-VL-4B-Instruct-ONNX",
                    SourceProvider = ModelSourceProvider.HuggingFace,
                    Tier = ModelTier.Direct,
                    Variants = new List<ModelVariant>
                    {
                        new ModelVariant
                        {
                            VariantId = "cuda",
                            TargetDirectoryPath = "onnxruntime/cuda/cuda-int4-rtn-block-32"
                        },
                        new ModelVariant
                        {
                            VariantId = "cpu",
                            TargetDirectoryPath = "onnxruntime/cpu_and_mobile/cpu-int4-rtn-block-32"
                        }
                    }
                }
            }
        };

        public SupportedModelDefinition? GetModel(string repoId)
        {
            if (Models.TryGetValue(repoId, out var model))
            {
                return model;
            }
            return null;
        }
    }
}