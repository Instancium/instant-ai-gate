namespace InstantAIGate.Core.Tests.Server;

using FluentAssertions;
using InstantAIGate.Core.Dtos.Config;
using InstantAIGate.Core.Dtos.Status;
using InstantAIGate.Core.Interfaces.Inference;
using InstantAIGate.Server.Diagnostics;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Moq;
using System.Threading.Tasks;
using Xunit;

public sealed class ModelReadyHealthCheckTests
{
    private readonly Mock<IModelManager> _mockModelManager;
    private readonly Mock<IGatewayStateManager> _mockStateManager;
    private readonly ModelReadyHealthCheck _healthCheck;

    public ModelReadyHealthCheckTests()
    {
        _mockModelManager = new Mock<IModelManager>();
        _mockStateManager = new Mock<IGatewayStateManager>();
        _healthCheck = new ModelReadyHealthCheck(_mockModelManager.Object, _mockStateManager.Object);
    }

    [Fact]
    public async Task CheckHealth_WhenModelIsLoaded_ReturnsHealthy()
    {
        _mockModelManager.Setup(m => m.GetActiveSettings())
            .Returns(new ModelSettings { RepoId = "active-model-8b" });
        _mockStateManager.Setup(s => s.GetSnapshot())
            .Returns(new GatewayStatusDetails { Status = GatewayOperationalStatus.Ready, ActiveModelId = "active-model-8b" });

        var context = new HealthCheckContext();
        var result = await _healthCheck.CheckHealthAsync(context);

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Contain("active-model-8b");
    }

    [Fact]
    public async Task CheckHealth_WhenModelIsDownloading_ReturnsDegradedWithMetrics()
    {
        _mockModelManager.Setup(m => m.GetActiveSettings()).Returns((ModelSettings?)null);
        _mockStateManager.Setup(s => s.GetSnapshot()).Returns(new GatewayStatusDetails
        {
            Status = GatewayOperationalStatus.ModelDownloading,
            ActiveModelId = "downloading-model",
            StageDescription = "Downloading chunk 1/4",
            ProgressPercentage = 35.5f,
            DownloadedBytes = 355_000_000,
            TotalBytes = 1_000_000_000,
            BytesPerSecond = 20_000_000
        });

        var context = new HealthCheckContext();
        var result = await _healthCheck.CheckHealthAsync(context);

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Contain("35.5%");
        result.Data.Should().ContainKey("progressPercentage");
        result.Data["progressPercentage"].Should().Be(35.5f);
    }

    [Fact]
    public async Task CheckHealth_WhenModelIsLoading_ReturnsDegraded()
    {
        _mockModelManager.Setup(m => m.GetActiveSettings()).Returns((ModelSettings?)null);
        _mockStateManager.Setup(s => s.GetSnapshot()).Returns(new GatewayStatusDetails
        {
            Status = GatewayOperationalStatus.ModelLoading,
            ActiveModelId = "loading-model",
            StageDescription = "Loading weights into VRAM",
            ProgressPercentage = 100.0f
        });

        var context = new HealthCheckContext();
        var result = await _healthCheck.CheckHealthAsync(context);

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Contain("Loading weights into VRAM");
    }

    [Fact]
    public async Task CheckHealth_WhenFaulted_ReturnsUnhealthyWithErrorMessage()
    {
        _mockModelManager.Setup(m => m.GetActiveSettings()).Returns((ModelSettings?)null);
        _mockStateManager.Setup(s => s.GetSnapshot()).Returns(new GatewayStatusDetails
        {
            Status = GatewayOperationalStatus.Faulted,
            ActiveModelId = "failed-model",
            ErrorMessage = "Disk out of space"
        });

        var context = new HealthCheckContext();
        var result = await _healthCheck.CheckHealthAsync(context);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("Disk out of space");
    }

    [Fact]
    public async Task CheckHealth_WhenUninitialized_ReturnsUnhealthy()
    {
        _mockModelManager.Setup(m => m.GetActiveSettings()).Returns((ModelSettings?)null);
        _mockStateManager.Setup(s => s.GetSnapshot()).Returns(new GatewayStatusDetails
        {
            Status = GatewayOperationalStatus.Uninitialized
        });

        var context = new HealthCheckContext();
        var result = await _healthCheck.CheckHealthAsync(context);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Be("No active model is loaded in memory.");
    }
}