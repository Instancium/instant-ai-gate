using InstantAIGate.Cli.State;
using Microsoft.Extensions.Logging;

namespace InstantAIGate.Cli.Logging
{
    /// <summary>
    /// Provider factory that creates instances of DebugStateLogger.
    /// </summary>
    public class DebugStateLoggerProvider : ILoggerProvider
    {
        private readonly DebugState _debugState;

        public DebugStateLoggerProvider(DebugState debugState)
        {
            _debugState = debugState;
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new DebugStateLogger(_debugState, categoryName);
        }

        public void Dispose()
        {
            // No unmanaged resources to release
        }
    }
}
