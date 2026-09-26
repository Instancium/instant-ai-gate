namespace InstantAIGate.Core.Exceptions;

using System;

public class QueueFullException : Exception
{
    public QueueFullException(int currentLimit)
        : base($"Gateway queue is at maximum capacity ({currentLimit}).")
    {
    }
}
