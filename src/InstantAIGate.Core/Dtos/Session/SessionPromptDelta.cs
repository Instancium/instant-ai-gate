namespace InstantAIGate.Core.Dtos.Session;

using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Dtos.Config;

public sealed record SessionPromptDelta(
    string SessionId,
    ChatMessage Message,
    InferenceSettings? OverrideSettings = null
);