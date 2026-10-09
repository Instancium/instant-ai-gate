namespace InstantAIGate.Core.Dtos.Session;

using InstantAIGate.Core.Dtos.Config;
using InstantAIGate.Core.Dtos.Inference;

public sealed record SessionPromptDelta(
    string SessionId,
    ChatMessage Message,
    InferenceSettings? OverrideSettings = null
);