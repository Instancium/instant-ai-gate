namespace InstantAIGate.Application.Dtos.Telemetry
{
    /// <summary>
    /// Hardware status metrics retrieved directly from NVML.
    /// </summary>
    public class GpuStatus
    {
        public double UsedMemoryGb { get; set; }
        public double TotalMemoryGb { get; set; }
        public int TemperatureCelsius { get; set; }
        public int UtilizationPercent { get; set; }
    }
}
