using InstantAIGate.API.Dtos;
using InstantAIGate.Application.Interfaces.Inference;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace InstantAiGate.Api.Controllers
{
    /// <summary>
    /// Handles requests for text embeddings following the OpenAI API specification.
    /// </summary>
    [ApiController]
    [Route("v1/embeddings")]
    public class OpenAiEmbeddingsController(IEmbeddingAdapter embeddingAdapter) : ControllerBase
    {
        /// <summary>
        /// Generates vector embeddings for the provided input text.
        /// </summary>
        /// <param name="request">The embedding request payload containing input text and model identifier.</param>
        /// <param name="ct">Cancellation token for the request.</param>
        /// <returns>An OpenAI-compliant embedding response.</returns>
        [HttpPost]
        public async Task<IActionResult> CreateEmbedding([FromBody] OpenAiEmbeddingRequest request, CancellationToken ct)
        {
            if (request?.Input == null || string.IsNullOrWhiteSpace(request.Model))
            {
                return BadRequest(new { error = "Invalid request. Both 'input' and 'model' parameters are required." });
            }

            try
            {
                var inputs = NormalizeInput(request.Input);

                if (inputs.Count == 0)
                {
                    return BadRequest(new { error = "Input text payload cannot be empty." });
                }

                IReadOnlyList<float[]> sentenceVectors = await embeddingAdapter.GetEmbeddingAsync(request.Model, inputs, ct);

                var embeddingDataList = new List<OpenAiEmbeddingData>(sentenceVectors.Count);

                for (int i = 0; i < sentenceVectors.Count; i++)
                {
                    embeddingDataList.Add(new OpenAiEmbeddingData(
                        Embedding: sentenceVectors[i] ?? Array.Empty<float>(),
                        Index: i
                    ));
                }

                int totalTokens = CalculateTotalTokens(inputs);

                var response = new OpenAiEmbeddingResponse
                {
                    model = request.Model,
                    data = embeddingDataList,
                    usage = new OpenAiUsage
                    {
                        PromptTokens = totalTokens,
                        TotalTokens = totalTokens,
                    }
                };

                return Ok(response);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { error = "ModelNotRegistered", message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = "ModelExecutionError", message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "EmbeddingGenerationError", message = ex.Message });
            }
        }

        /// <summary>
        /// Normalizes flexible JSON input into a uniform list of strings.
        /// Accommodates both single string and array of strings as per OpenAI specification.
        /// </summary>
        private static List<string> NormalizeInput(object input)
        {
            var list = new List<string>();

            if (input is JsonElement jsonElement)
            {
                if (jsonElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in jsonElement.EnumerateArray())
                    {
                        var str = item.GetString();
                        if (str != null) list.Add(str);
                    }
                }
                else if (jsonElement.ValueKind == JsonValueKind.String)
                {
                    var str = jsonElement.GetString();
                    if (str != null) list.Add(str);
                }
            }
            else if (input is string singleString)
            {
                list.Add(singleString);
            }
            else if (input is string[] arrayStrings)
            {
                list.AddRange(arrayStrings);
            }

            return list;
        }

        /// <summary>
        /// Approximates token count based on string length.
        /// Assumes ~4 characters per token as a standard heuristic.
        /// </summary>
        private static int CalculateTotalTokens(IEnumerable<string> inputs)
        {
            int tokenCount = 0;
            foreach (var text in inputs.Where(text => !string.IsNullOrWhiteSpace(text)))
            {
                tokenCount += (int)Math.Ceiling(text.Length / 4.0);
            }
            return tokenCount;
        }
    }
}