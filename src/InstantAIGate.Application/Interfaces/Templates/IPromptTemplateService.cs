using InstantAIGate.Application.Dtos.Requests;

namespace InstantAIGate.Application.Interfaces.Templates
{
    public interface IPromptTemplateService
    {
        /// <summary>
        /// Converts a list of messages into a prompt string according to the model's format.
        /// </summary>
        string BuildPrompt(IEnumerable<ChatMessage> messages);
    }
}
