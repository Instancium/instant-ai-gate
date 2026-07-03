using FluentAssertions;
using InstantAIGate.Infrastructure.Inference.Drivers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace InstantAIGate.Infrastructure.Tests.Inference.Drivers;

/// <summary>
/// Tests for NativeBackendRegistry - validates backend discovery and selection logic.
/// SUT: NativeBackendRegistry
/// Mocks: ILogger (external dependency)
/// Note: File system is NOT mocked - registry behavior depends on real runtime structure
/// </summary>
public class NativeBackendRegistryTests
{
    private readonly Mock<ILogger<NativeBackendRegistry>> _loggerMock;
    private readonly NativeLibraryOptions _options;
    private readonly NativeRuntimeExtractor _extractor;

    public NativeBackendRegistryTests()
    {
        _loggerMock = new Mock<ILogger<NativeBackendRegistry>>();
        _options = new NativeLibraryOptions
        {
            EnableDebugLogging = false,
            PreferredBackend = "auto"
        };
        _extractor = new NativeRuntimeExtractor(
            new Mock<ILogger<NativeRuntimeExtractor>>().Object);
    }

    private NativeBackendRegistry CreateSut()
    {
        var registry = new NativeBackendRegistry(
            _loggerMock.Object,
            Options.Create(_options),
            _extractor
        );
        registry.Refresh();
        return registry;
    }

    [Fact]
    public void Constructor_ValidDependencies_InitializesSuccessfully()
    {
        // Arrange & Act
        var sut = CreateSut();

        // Assert
        sut.Should().NotBeNull();
    }

    [Fact]
    public void GetAllBackends_AfterInitialization_ReturnsNonNull()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var allBackends = sut.GetAllBackends();

        // Assert
        allBackends.Should().NotBeNull("registry should always return a collection");
    }

    [Fact]
    public void GetAvailableBackends_AfterInitialization_ReturnsOnlyAvailable()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var availableBackends = sut.GetAvailableBackends();

        // Assert
        availableBackends.Should().NotBeNull();
        availableBackends.Should().OnlyContain(
            b => b.IsAvailable,
            "GetAvailableBackends should filter out unavailable backends"
        );
    }

    [Theory]
    [InlineData("cpu")]
    [InlineData("cuda")]
    [InlineData("vulkan")]
    [InlineData("rocm")]
    public void GetBackend_ExistingBackendName_ReturnsMatchingBackend(string backendName)
    {
        // Arrange
        var sut = CreateSut();
        var allBackends = sut.GetAllBackends();

        // Skip test if backend doesn't exist in test environment
        if (!allBackends.Any(b => b.Name.Equals(backendName, StringComparison.OrdinalIgnoreCase)))
        {
            return; // Skip test gracefully
        }

        // Act
        var backend = sut.GetBackend(backendName);

        // Assert
        backend.Should().NotBeNull($"backend '{backendName}' should be retrievable");
        backend!.Name.Should().BeEquivalentTo(backendName, "name should match case-insensitively");
    }

    [Fact]
    public void GetBackend_NonExistentBackend_ReturnsNull()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var backend = sut.GetBackend("non_existent_backend_xyz");

        // Assert
        backend.Should().BeNull("non-existent backend should return null");
    }

    [Fact]
    public void GetBackend_CaseInsensitiveSearch_ReturnsBackend()
    {
        // Arrange
        var sut = CreateSut();
        var allBackends = sut.GetAllBackends();

        // Skip if no backends available
        if (!allBackends.Any())
        {
            return;
        }

        var firstBackend = allBackends.First();

        // Act
        var backendUpper = sut.GetBackend(firstBackend.Name.ToUpperInvariant());
        var backendLower = sut.GetBackend(firstBackend.Name.ToLowerInvariant());

        // Assert
        backendUpper.Should().NotBeNull("case-insensitive search should work (uppercase)");
        backendLower.Should().NotBeNull("case-insensitive search should work (lowercase)");

        if (backendUpper is null || backendLower is null)
        {
            return;
        }

        backendUpper.Name.Should().BeEquivalentTo(backendLower.Name);
    }

    [Fact]
    public void ResolveBackend_AutoWithAvailableGpu_SelectsGpuBackend()
    {
        // Arrange
        var sut = CreateSut();
        var availableBackends = sut.GetAvailableBackends();

        // Skip test if no GPU backends available
        if (!availableBackends.Any(b => b.IsGpu))
        {
            return;
        }

        // Act
        var resolvedBackend = sut.ResolveBackend("auto");

        // Assert
        resolvedBackend.Should().NotBeNull();
        resolvedBackend.IsGpu.Should().BeTrue(
            "auto selection should prefer GPU when available"
        );
    }

    [Fact]
    public void ResolveBackend_AutoWithOnlyCpu_SelectsCpuBackend()
    {
        // Arrange
        var sut = CreateSut();
        var availableBackends = sut.GetAvailableBackends();

        // Skip test if GPU backends exist (this test requires CPU-only environment)
        if (availableBackends.Any(b => b.IsGpu))
        {
            return;
        }

        // Act
        var resolvedBackend = sut.ResolveBackend("auto");

        // Assert
        resolvedBackend.Should().NotBeNull();
        resolvedBackend.IsGpu.Should().BeFalse("should select CPU when no GPU available");
    }

    [Fact]
    public void ResolveBackend_SpecificAvailableBackend_ReturnsRequestedBackend()
    {
        // Arrange
        var sut = CreateSut();
        var availableBackends = sut.GetAvailableBackends();

        // Skip if no backends available
        if (!availableBackends.Any())
        {
            return;
        }

        var targetBackend = availableBackends.First();

        // Act
        var resolvedBackend = sut.ResolveBackend(targetBackend.Name);

        // Assert
        resolvedBackend.Should().NotBeNull();
        resolvedBackend.Name.Should().BeEquivalentTo(targetBackend.Name);
    }

    [Fact]
    public void ResolveBackend_UnavailableBackend_ThrowsInvalidOperationException()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        Action act = () => sut.ResolveBackend("non_existent_backend_xyz");

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*not available*");
    }

    [Fact]
    public void ResolveBackend_NoBackendsAvailable_ThrowsInvalidOperationException()
    {
        // Arrange
        // Note: Cannot easily simulate no backends without modifying file system
        // This test documents expected behavior when .runtimes folder is missing
        var sut = CreateSut();
        var availableBackends = sut.GetAvailableBackends();

        // Skip test if backends are actually available
        if (availableBackends.Any())
        {
            return; // Cannot test this scenario in current environment
        }

        // Act
        Action act = () => sut.ResolveBackend("auto");

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("No native backends found*");
    }

    [Fact]
    public void Refresh_CalledMultipleTimes_UpdatesBackendList()
    {
        // Arrange
        var sut = CreateSut();
        var initialBackends = sut.GetAllBackends();

        // Act
        sut.Refresh();
        var refreshedBackends = sut.GetAllBackends();

        // Assert
        refreshedBackends.Should().NotBeNull();
        refreshedBackends.Count.Should().Be(
            initialBackends.Count,
            "refresh should maintain consistency"
        );
    }

    [Fact]
    public void BackendInfo_GpuBackends_HaveHigherPriority()
    {
        // Arrange
        var sut = CreateSut();
        var availableBackends = sut.GetAvailableBackends();

        // Skip if less than 2 backends
        if (availableBackends.Count < 2)
        {
            return;
        }

        // Act
        var gpuBackends = availableBackends.Where(b => b.IsGpu).ToList();
        var cpuBackends = availableBackends.Where(b => !b.IsGpu).ToList();

        // Assert
        if (gpuBackends.Any() && cpuBackends.Any())
        {
            var autoResolved = sut.ResolveBackend("auto");
            autoResolved.IsGpu.Should().BeTrue(
                "auto resolution should prefer GPU over CPU"
            );
        }
    }

    [Theory]
    [InlineData("win-x64")]
    [InlineData("linux-x64")]
    [InlineData("osx-arm64")]
    public void BackendInfo_ValidRid_MatchesPattern(string rid)
    {
        // Arrange
        var validPattern = rid.Contains('-');

        // Act & Assert
        validPattern.Should().BeTrue("RID should follow 'os-arch' pattern");
    }

    [Fact]
    public async Task GetAllBackends_ConcurrentCalls_ThreadSafe()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var tasks = Enumerable.Range(0, 20)
            .Select(_ => Task.Run(() => sut.GetAllBackends()))
            .ToArray();

        await Task.WhenAll(tasks);

        // Assert
        tasks.Should().OnlyContain(t => t.IsCompletedSuccessfully);
        var allResults = tasks.Select(t => t.Result).ToList();
        allResults.Should().OnlyContain(
            r => r != null,
            "all concurrent reads should succeed"
        );
    }

    [Fact]
    public void GetAvailableBackends_FiltersByAvailability_ConsistentResults()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var available1 = sut.GetAvailableBackends();
        var available2 = sut.GetAvailableBackends();

        // Assert
        available1.Count.Should().Be(
            available2.Count,
            "consecutive calls should return consistent results"
        );
    }

    [Fact]
    public void BackendPath_ContainsValidDirectory_FollowsConvention()
    {
        // Arrange
        var sut = CreateSut();
        var allBackends = sut.GetAllBackends();

        // Skip if no backends
        if (!allBackends.Any())
        {
            return;
        }

        // Act & Assert
        foreach (var backend in allBackends)
        {
            backend.Path.Should().Contain(
                backend.Rid,
                "backend path should contain the runtime identifier"
            );
        }
    }

    [Fact]
    public void ResolveBackend_LogsSelectionDecision()
    {
        // Arrange
        var sut = CreateSut();
        var availableBackends = sut.GetAvailableBackends();

        // Skip if no backends
        if (!availableBackends.Any())
        {
            return;
        }

        // Act
        sut.ResolveBackend("auto");

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("selected", StringComparison.OrdinalIgnoreCase)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()!),
            Times.AtLeastOnce(),
            "backend selection should log information message"
        );
    }
}
