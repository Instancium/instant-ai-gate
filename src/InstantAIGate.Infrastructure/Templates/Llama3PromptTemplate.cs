using InstantAIGate.Application.Dtos.Requests;
using InstantAIGate.Application.Interfaces.Templates;
using System.Text;

namespace InstantAIGate.Infrastructure.Templates
{
    public class Llama3PromptTemplate : IPromptTemplateService
    {
        public string BuildPrompt(IEnumerable<ChatMessage> messages)
        {
            var sb = new StringBuilder();
            foreach (var msg in messages)
            {
                sb.Append($"<|start_header_id|>{msg.Role}<|end_header_id|>\n\n{msg.Content}<|eot_id|>");
            }
            sb.Append("<|start_header_id|>assistant<|end_header_id|>\n\n");
            return sb.ToString();
        }
    }
}
