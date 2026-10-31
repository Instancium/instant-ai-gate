namespace InstantAIGate.Dashboard.Services;

public sealed record LogEntry(
    DateTimeOffset Timestamp,
    string Level,
    string Category,
    string Message);
