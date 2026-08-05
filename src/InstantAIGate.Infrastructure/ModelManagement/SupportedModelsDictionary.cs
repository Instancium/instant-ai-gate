using InstantAIGate.Application.ModelManagement.Conteracts;
using InstantAIGate.Domain.Entities;
using InstantAIGate.Domain.Enums;
using System.Collections.Generic;

namespace InstantAIGate.Infrastructure.ModelManagement
{
    public class SupportedModelsDictionary : IModelCatalog
    {
        public static readonly IReadOnlyDictionary<string, SupportedModelDefinition> Models = new Dictionary<string, SupportedModelDefinition>
        {
            {
                "onnx-community/Llama-3-8B-Instruct",
                new SupportedModelDefinition
                {
                    RepoId = "onnx-community/Llama-3-8B-Instruct",
                    TargetDirectoryPath = "onnx/cuda-int4",
                    SourceProvider = ModelSourceProvider.HuggingFace,
                    Tier = ModelTier.Direct
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