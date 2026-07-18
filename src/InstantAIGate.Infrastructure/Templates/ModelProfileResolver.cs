using InstantAIGate.Application.Interfaces.Templates;

namespace InstantAIGate.Infrastructure.Templates;

public static class ModelProfileResolver
{
    public record ModelConfiguration(IPromptTemplateService Template, string[] AntiPrompts);

    public static ModelConfiguration Resolve(string modelPath)
    {
        var fileName = Path.GetFileNameWithoutExtension(modelPath).ToLowerInvariant();

        // Logic to determine profile based on filename (extendable)
        if (fileName.Contains("qwen2") || fileName.Contains("qwen3"))
        {
            return new ModelConfiguration(
                new QwenPromptTemplate(),
                new[] { "<|im_end|>", "<|endoftext|>" }
            );
        }

        if (fileName.Contains("llama-3") || fileName.Contains("llama3"))
        {
            return new ModelConfiguration(
                new Llama3PromptTemplate(),
                new[] { "<|eot_id|>", "<|end_of_text|>" }
            );
        }

        // Default
        return new ModelConfiguration(new RawPromptTemplate(), new[] { "</s>" });
    }
}