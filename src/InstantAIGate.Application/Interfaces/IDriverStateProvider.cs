namespace InstantAIGate.Application.Interfaces
{
    /// <summary>
    /// Provides thread-safe state tracking for native driver initialization.
    /// </summary>
    public interface IDriverStateProvider
    {
        bool IsExtracting { get; }
        void BeginExtraction();
        void EndExtraction();
    }
}
