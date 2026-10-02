namespace InstantAIGate.Core.Exceptions;

using System;

public sealed class ContextOverflowException : Exception
{
    public string SessionId { get; }
    public int PastTokens { get; }
    public int IncomingTokens { get; }
    public int ReservedTokens { get; }
    public int ContextSize { get; }

    public ContextOverflowException(string sessionId, int pastTokens, int incomingTokens, int reservedTokens, int contextSize)
        : base($"Context window exceeded for session '{sessionId}'. " +
               $"Tokens: (Past: {pastTokens} + Incoming: {incomingTokens} + Reserve: {reservedTokens}) > ContextSize: {contextSize}.")
    {
        SessionId = sessionId;
        PastTokens = pastTokens;
        IncomingTokens = incomingTokens;
        ReservedTokens = reservedTokens;
        ContextSize = contextSize;
    }
}