using InstantAIGate.Application.Dtos.Requests;
using InstantAIGate.Application.Interfaces.Templates;
using System.Text;

namespace InstantAIGate.Infrastructure.Templates
{
    public class QwenPromptTemplate : IPromptTemplateService
    {
        public string BuildPrompt(IEnumerable<ChatMessage> messages)
        {
            var sb = new StringBuilder();
            foreach (var msg in messages)
            {
                var role = msg.Role.ToLowerInvariant();
                if (role is not ("system" or "user" or "assistant")) role = "user";

                sb.Append($"<|im_start|>{role}\n");
                sb.Append(msg.Content);
                sb.Append("\n<|im_end|>\n");
            }
            sb.Append("<|im_start|>assistant\n");
            return sb.ToString();
        }
    }
}
