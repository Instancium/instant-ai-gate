namespace InstantAIGate.Application.Dtos.Requests
{
    /// <summary>
    /// Represents a chat completion request for LLM inference.
    /// Contains all parameters needed for generating AI responses, including both
    /// OpenAI-compatible parameters and llama.cpp-specific settings.
    /// </summary>
    public record ChatRequest
    {
        #region Core Parameters

        /// <summary>
        /// The unique identifier of the model to be used for generating the response.
        /// Determines model capabilities, context window size, and tokenization behavior.
        /// </summary>
        public string? Model { get; init; }

        /// <summary>
        /// A list of messages comprising the conversation history or input prompt.
        /// Each message contains a role (system, user, assistant) and content.
        /// This is the primary input that drives the model's response generation.
        /// </summary>
        public List<ChatMessage> Messages { get; init; } = new();

        #endregion

        #region Sampling Parameters

        /// <summary>
        /// Controls the randomness of the output. Higher values (e.g., 1.0) make the output more random 
        /// and creative, while lower values (e.g., 0.2) make it more deterministic and focused.
        /// Affects the probability distribution used during token selection in sampling.
        /// </summary>
        public float Temperature { get; init; } = 1.0f;

        /// <summary>
        /// An alternative to temperature, known as nucleus sampling. The model considers the results 
        /// of the tokens with top_p probability mass. For example, 0.1 means only tokens comprising 
        /// the top 10% probability mass are considered. 1.0 means no filtering.
        /// Lower values make output more focused, higher values allow more diversity.
        /// Works in conjunction with temperature to control output variability.
        /// </summary>
        public float TopP { get; init; } = 1.0f;

        /// <summary>
        /// Limits the model to select the next token from the top K most likely candidates.
        /// This is a llama.cpp-specific parameter not present in OpenAI API.
        /// Lower values (e.g., 10) produce more deterministic output, higher values (e.g., 100) increase diversity.
        /// Helps control vocabulary breadth during generation.
        /// </summary>
        public int TopK { get; init; } = 40;

        /// <summary>
        /// The maximum number of tokens to generate in the completion.
        /// Defines the upper limit of the response length to prevent excessive generation.
        /// The actual token count depends on the tokenizer used by the specific model.
        /// </summary>
        public int MaxTokens { get; init; } = 4096;

        /// <summary>
        /// Number of completion choices to generate for each request.
        /// Allows generating multiple alternative responses in a single inference call.
        /// Useful for A/B testing different outputs or selecting the best response.
        /// Increases compute usage proportionally to the number of choices requested.
        /// </summary>
        public int N { get; init; } = 1;

        /// <summary>
        /// Sequences where the model will stop generating further tokens.
        /// The response will not contain the stop sequence(s).
        /// Useful for controlling output format or terminating at specific markers.
        /// Can specify multiple stop sequences for complex termination logic.
        /// </summary>
        public List<string>? Stop { get; init; }

        #endregion

        #region Penalty Parameters

        /// <summary>
        /// Positive values penalize new tokens based on whether they have appeared in the text so far, 
        /// encouraging the model to talk about new topics. Range: -2.0 to 2.0.
        /// Higher values increase topic diversity and prevent theme repetition.
        /// Helps maintain conversation freshness in multi-turn interactions.
        /// </summary>
        public float PresencePenalty { get; init; } = 1.1f;

        /// <summary>
        /// Positive values penalize new tokens based on their existing frequency in the text so far, 
        /// decreasing the likelihood of the model repeating the exact same lines verbatim.
        /// Range: -2.0 to 2.0. More effective than presence_penalty for reducing exact word repetitions.
        /// </summary>
        public float FrequencyPenalty { get; init; } = 1.1f;

        /// <summary>
        /// A penalty applied to discourage the model from repeating the same tokens or sequences.
        /// This is a llama.cpp-specific parameter. A value of 1.0 indicates no penalty is applied.
        /// Values greater than 1.0 increase repetition penalty, values less than 1.0 decrease it.
        /// More granular control over repetition behavior compared to presence/frequency penalties.
        /// </summary>
        public float RepeatPenalty { get; init; } = 1.0f;

        #endregion

        #region Deterministic Control

        /// <summary>
        /// A seed value used to make the model's output deterministic.
        /// If set, the model will attempt to produce the same output given the same input and parameters.
        /// Useful for debugging, testing, and reproducible experiments.
        /// Note: Determinism is not guaranteed in all cases due to system optimizations.
        /// </summary>
        public int? Seed { get; init; }

        #endregion

        #region Log Probabilities

        /// <summary>
        /// Whether to return log probabilities of the output tokens.
        /// If true, returns the log probability of each token in the response.
        /// Useful for analyzing model confidence and understanding token selection.
        /// Adds computational overhead and increases response size.
        /// </summary>
        public bool Logprobs { get; init; } = false;

        /// <summary>
        /// Number of most likely tokens to return at each position, along with their log probabilities.
        /// Only applicable when Logprobs is true. Range: 0 to 20.
        /// Provides insight into alternative token choices the model considered.
        /// Useful for understanding model uncertainty and exploring different generation paths.
        /// </summary>
        public int TopLogprobs { get; init; } = 0;

        /// <summary>
        /// Modifies the likelihood of specified tokens appearing in the completion.
        /// Maps token IDs (as strings) to bias values from -100 to 100.
        /// Positive values increase likelihood, negative values decrease it.
        /// Useful for forcing or preventing specific words, phrases, or formatting.
        /// </summary>
        public Dictionary<string, int>? LogitBias { get; init; }

        #endregion

        #region Streaming

        /// <summary>
        /// If true, the model will stream output tokens as they are generated.
        /// Uses Server-Sent Events (SSE) format for incremental delivery.
        /// Reduces time-to-first-token and improves perceived responsiveness.
        /// Essential for real-time chat applications and progressive rendering.
        /// </summary>
        public bool Stream { get; init; } = false;

        /// <summary>
        /// Options for configuring streaming behavior.
        /// Can include settings like IncludeUsage to add token usage statistics to the stream.
        /// Allows fine-grained control over what data is included in streaming responses.
        /// Only applicable when Stream is set to true.
        /// </summary>
        public StreamOptions? StreamOptions { get; init; }

        #endregion

        #region Tools and Function Calling

        /// <summary>
        /// List of tools (functions) available for the model to call.
        /// Each tool defines a function name, description, and parameter schema.
        /// Enables the model to interact with external systems and APIs.
        /// The model can choose to call one or more tools based on the conversation context.
        /// </summary>
        public List<ToolDefinition>? Tools { get; init; }

        /// <summary>
        /// Controls which tool the model should call.
        /// Can be "none" (no tool calls), "auto" (model decides), "required" (must call a tool),
        /// or a specific tool name to force that particular tool.
        /// Provides explicit control over tool invocation behavior.
        /// </summary>
        public ToolChoiceOption? ToolChoice { get; init; }

        /// <summary>
        /// Whether to enable parallel function calling during tool use.
        /// If true, the model can call multiple tools in a single response.
        /// If false, the model will call at most one tool per response.
        /// Useful for optimizing workflows that require multiple independent tool calls.
        /// </summary>
        public bool ParallelToolCalls { get; init; } = true;

        #endregion

        #region Response Format

        /// <summary>
        /// Specifies the format the model must output.
        /// Can be {"type": "text"} for default behavior or {"type": "json_object"} for JSON mode.
        /// JSON mode ensures the output is valid JSON, useful for structured data extraction.
        /// Requires the model to be fine-tuned or prompted appropriately for reliable JSON output.
        /// Can also support GBNF grammar for custom structured output formats.
        /// </summary>
        public ResponseFormat? ResponseFormat { get; init; }

        #endregion

        #region User and Tracking

        /// <summary>
        /// A unique identifier representing the end-user making the request.
        /// Helps monitor and detect abuse, and enables usage tracking per user.
        /// Should be a hash or anonymized identifier to protect user privacy.
        /// Recommended for all production applications to enable better support and monitoring.
        /// </summary>
        public string? User { get; init; }

        /// <summary>
        /// A unique identifier for safety and abuse monitoring purposes.
        /// Used to track and prevent misuse of the API.
        /// Helps maintain service quality and compliance with usage policies.
        /// </summary>
        public string? SafetyIdentifier { get; init; }

        /// <summary>
        /// A unique key used for prompt caching to improve performance and reduce costs.
        /// Identical prompts with the same cache key can be served from cache.
        /// Significantly reduces latency for repeated or similar requests.
        /// Useful for applications with high request volumes and repetitive patterns.
        /// </summary>
        public string? PromptCacheKey { get; init; }

        #endregion

        #region Storage and Metadata

        /// <summary>
        /// Whether to store the request and response for model distillation or evaluation.
        /// If true, the data may be used to improve future model versions.
        /// Should be set based on data privacy and retention policies.
        /// </summary>
        public bool Store { get; init; } = false;

        /// <summary>
        /// Custom key-value pairs for storing metadata about the request.
        /// Useful for tracking, analytics, and debugging purposes.
        /// Can include information like request source, user session, or experiment ID.
        /// Does not affect model behavior but helps with request management.
        /// </summary>
        public Dictionary<string, string>? Metadata { get; init; }

        #endregion

        #region Service Configuration

        /// <summary>
        /// Specifies the service tier for processing the request.
        /// Can be "auto" (default), "default", or "flex" for different processing priorities.
        /// Flex tier may offer lower costs with variable latency.
        /// Allows optimization of cost-performance tradeoffs based on application requirements.
        /// </summary>
        public string? ServiceTier { get; init; }

        #endregion
    }

    #region Supporting Types

    /// <summary>
    /// Represents a tool definition for function calling.
    /// </summary>
    public record ToolDefinition
    {
        /// <summary>
        /// The type of tool. Currently only "function" is supported.
        /// </summary>
        public string Type { get; init; } = "function";

        /// <summary>
        /// The function definition including name, description, and parameters.
        /// </summary>
        public FunctionDefinition? Function { get; init; }
    }

    /// <summary>
    /// Defines a function that the model can call.
    /// </summary>
    public record FunctionDefinition
    {
        /// <summary>
        /// The name of the function to be called. Must be a-z, A-Z, 0-9, or contain underscores and dashes.
        /// </summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>
        /// A description of what the function does, used by the model to choose when and how to call it.
        /// </summary>
        public string? Description { get; init; }

        /// <summary>
        /// The parameters the functions accepts, described as a JSON Schema object.
        /// </summary>
        public object? Parameters { get; init; }
    }

    /// <summary>
    /// Controls which tool the model should call.
    /// </summary>
    public record ToolChoiceOption
    {
        /// <summary>
        /// The type of tool choice. Can be "none", "auto", "required", or "function".
        /// </summary>
        public string Type { get; init; } = "auto";

        /// <summary>
        /// If type is "function", specifies which function to call.
        /// </summary>
        public ToolFunction? Function { get; init; }
    }

    /// <summary>
    /// Specifies a specific function to call.
    /// </summary>
    public record ToolFunction
    {
        /// <summary>
        /// The name of the function to call.
        /// </summary>
        public string Name { get; init; } = string.Empty;
    }

    /// <summary>
    /// Specifies the format of the model's response.
    /// </summary>
    public record ResponseFormat
    {
        /// <summary>
        /// The type of response format. Can be "text" or "json_object".
        /// </summary>
        public string Type { get; init; } = "text";

        /// <summary>
        /// GBNF grammar string for custom structured output (llama.cpp specific).
        /// Allows defining complex output formats beyond simple JSON.
        /// </summary>
        public string? Grammar { get; init; }
    }

    /// <summary>
    /// Options for configuring streaming behavior.
    /// </summary>
    public record StreamOptions
    {
        /// <summary>
        /// If true, includes usage statistics in the streaming response.
        /// Adds token count information to the final stream chunk.
        /// </summary>
        public bool IncludeUsage { get; init; } = false;
    }

    #endregion
}

