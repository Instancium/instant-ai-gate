using InstantAIGate.Application.Interfaces;

namespace InstantAIGate.Infrastructure.Inference.Drivers
{
    internal sealed class DriverStateProvider : IDriverStateProvider
    {
        private int _isExtracting;

        public bool IsExtracting => Volatile.Read(ref _isExtracting) == 1;

        public void BeginExtraction() => Volatile.Write(ref _isExtracting, 1);

        public void EndExtraction() => Volatile.Write(ref _isExtracting, 0);
    }
}
