namespace InstantAIGate.Application.Dtos.Telemetry
{
    /// <summary>
    /// Root container for comprehensive system telemetry data.
    /// </summary>
    public class SystemTelemetry
    {
        public GpuStatus Gpu { get; set; } = new();
        public SystemHardwareStatus System { get; set; } = new();
        public List<ModelTelemetry> Models { get; set; } = new();
        public bool IsExtractingDrivers { get; set; }
    }


}