namespace InstantAIGate.Core.Dtos.Session;

using InstantAIGate.Core.Exceptions;
using System;

/// <summary>
/// Wire-safe projection of <see cref="ContextOverflowException"/> for SignalR transport.
///
/// RATIONALE: System.Text.Json cannot serialize System.Exception derivatives — the
/// inherited TargetSite property is of type System.Reflection.MethodBase, which has
/// no supported converter. Sending the raw exception over a SignalR hub aborts the
/// connection mid-write ("Failed writing message. Aborting connection."), which on
/// auto-reconnecting clients orphans the in-flight stream and manifests as an
/// infinite hang with no client-side error.
///
/// This record contains only primitive members and is fully serializable.
/// </summary>
public sealed record ContextOverflowReport(
    string SessionId,
    int PastTokens,
    int IncomingTokens,
    int ReservedTokens,
    int ContextSize)
{
    public static ContextOverflowReport FromException(ContextOverflowException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return new ContextOverflowReport(
            exception.SessionId,
            exception.PastTokens,
            exception.IncomingTokens,
            exception.ReservedTokens,
            exception.ContextSize);
    }

    public ContextOverflowException ToException() =>
        new(SessionId, PastTokens, IncomingTokens, ReservedTokens, ContextSize);
}
