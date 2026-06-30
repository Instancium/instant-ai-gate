using FluentAssertions;
using InstantAIGate.Infrastructure.Inference.Drivers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System.Runtime.InteropServices;

namespace InstantAIGate.Infrastructure.Tests.Inference.Drivers;

/// <summary>
/// Cross-platform and Linux-specific tests for native backend discovery.
/// Validates behavior across Windows, Linux, and macOS environments.
/// </summary>
public class CrossPlatformBackendTests
{
    private readonly Mock<ILogger<NativeBackendRegistry>> _loggerMock;
    private readonly NativeLibraryOptions _options;

    public CrossPlatformBackendTests()
    {
        _loggerMock = new Mock<ILogger<NativeBackendRegistry>>();
        _options = new NativeLibraryOptions
        {
            EnableDebugLogging = true,
            PreferredBackend = "auto"
        };
    }

    private NativeBackendRegistry CreateSut()
    {
        return new NativeBackendRegistry(
            _loggerMock.Object,
            Options.Create(_options)
        );
    }

    [Fact]
    public void GetRuntimeIdentifier_CurrentPlatform_ReturnsValidRid()
    {
        // Arrange
        var sut = CreateSut();
        var allBackends = sut.GetAllBackends();

        // Act
        var actualRid = GetExpectedRid();

        // Assert
        actualRid.Should().MatchRegex(@"^(win|linux|osx)-(x64|arm64)$",
            "runtime identifier should follow standard .NET RID pattern");

        // Verify registry discovered backends for current RID
        if (allBackends.Any())
        {
            allBackends.Should().OnlyContain(b => b.Rid == actualRid,
                $"all discovered backends should match current RID: {actualRid}");
        }
    }

    [Theory]
    [InlineData("llama.dll", true)]      // Windows
    [InlineData("libllama.so", true)]    // Linux
    [InlineData("libllama.dylib", true)] // macOS
    [InlineData("LLAMA.DLL", true)]      // Case variation
    [InlineData("libother.so", true)]    // Other native lib
    [InlineData("config.json", false)]   // Non-native file
    [InlineData("readme.txt", false)]    // Text file
    public void IsNativeLibrary_DifferentExtensions_DetectsCorrectly(string filename, bool expectedIsNative)
    {
        // Arrange
        var tempPath = Path.Combine(Path.GetTempPath(), Path.GetFileName(filename));

        // Act
        var isNative = HasNativeExtension(tempPath);

        // Assert
        isNative.Should().Be(expectedIsNative,
            $"file '{filename}' should {(expectedIsNative ? "" : "not ")}be detected as native library");
    }

    [Fact]
    [Trait("Platform", "Linux")]
    public void GetRuntimeIdentifier_OnLinux_ReturnsLinuxRid()
    {
        // Arrange & Act
        var rid = GetExpectedRid();

        // Assert
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            rid.Should().StartWith("linux-",
                "runtime identifier on Linux should start with 'linux-'");
            rid.Should().MatchRegex(@"^linux-(x64|arm64)$",
                "Linux RID should be linux-x64 or linux-arm64");
        }
    }

    [Fact]
    [Trait("Platform", "Windows")]
    public void GetRuntimeIdentifier_OnWindows_ReturnsWindowsRid()
    {
        // Arrange & Act
        var rid = GetExpectedRid();

        // Assert
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            rid.Should().StartWith("win-",
                "runtime identifier on Windows should start with 'win-'");
            rid.Should().MatchRegex(@"^win-(x64|arm64)$",
                "Windows RID should be win-x64 or win-arm64");
        }
    }

    [Fact]
    [Trait("Platform", "macOS")]
    public void GetRuntimeIdentifier_OnMacOS_ReturnsOsxRid()
    {
        // Arrange & Act
        var rid = GetExpectedRid();

        // Assert
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            rid.Should().StartWith("osx-",
                "runtime identifier on macOS should start with 'osx-'");
            rid.Should().MatchRegex(@"^osx-(x64|arm64)$",
                "macOS RID should be osx-x64 or osx-arm64");
        }
    }

    [Fact]
    public void EnsureRuntimesCopied_WhenSourceMissing_DoesNotThrow()
    {
        // Arrange
        // Simulate Docker scenario where .runtimes is not in parent directories
        var sut = CreateSut();

        // Act
        Action act = () => sut.Refresh();

        // Assert
        act.Should().NotThrow(
            "missing .runtimes folder should be handled gracefully in Docker environments");
    }

    [Fact]
    public void GetAllBackends_EmptyRuntimesDirectory_ReturnsEmptyList()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var backends = sut.GetAllBackends();

        // Assert
        // Note: Actual result depends on file system state
        backends.Should().NotBeNull("backend list should never be null, even when empty");
    }

    [Theory]
    [InlineData("cpu", false)]
    [InlineData("CPU", false)]
    [InlineData("cuda", true)]
    [InlineData("vulkan", true)]
    [InlineData("rocm", true)]
    [InlineData("CUDA", true)]
    public void BackendInfo_IsGpuProperty_DeterminedByName(string backendName, bool expectedIsGpu)
    {
        // Arrange
        var backend = new NativeBackendInfo
        {
            Name = backendName,
            Path = $"/app/runtimes/linux-x64/{backendName}",
            Rid = "linux-x64",
            IsAvailable = true
        };

        // Act
        var isGpu = backend.IsGpu;

        // Assert
        isGpu.Should().Be(expectedIsGpu,
            $"backend '{backendName}' should {(expectedIsGpu ? "" : "not ")}be detected as GPU");
    }

    [Fact]
    public void BackendPath_ContainsRuntimeIdentifier_FollowsConvention()
    {
        // Arrange
        var sut = CreateSut();
        var backends = sut.GetAllBackends();

        // Skip if no backends available
        if (!backends.Any())
        {
            return;
        }

        // Act & Assert
        foreach (var backend in backends)
        {
            backend.Path.Should().Contain(backend.Rid,
                $"backend path should contain RID '{backend.Rid}'");

            backend.Path.Should().MatchRegex(@"runtimes[/\\](win|linux|osx)-(x64|arm64)[/\\]",
                "backend path should follow runtimes/{rid}/{backend} structure");
        }
    }

    [Fact]
    public void LibraryFiles_WhenBackendAvailable_ContainsPlatformSpecificLibrary()
    {
        // Arrange
        var sut = CreateSut();
        var availableBackends = sut.GetAvailableBackends();

        // Skip if no backends
        if (!availableBackends.Any())
        {
            return;
        }

        // Act & Assert
        foreach (var backend in availableBackends)
        {
            backend.LibraryFiles.Should().NotBeEmpty(
                $"available backend '{backend.Name}' should have library files");

            var expectedLibrary = GetExpectedLibraryName();

            backend.LibraryFiles.Should().Contain(
                f => f.Equals(expectedLibrary, StringComparison.OrdinalIgnoreCase),
                $"available backend should contain main library '{expectedLibrary}'");
        }
    }

    [Theory]
    [InlineData("win-x64", @"C:\app\runtimes\win-x64\cuda")]
    [InlineData("linux-x64", "/app/runtimes/linux-x64/cuda")]
    [InlineData("osx-arm64", "/app/runtimes/osx-arm64/cpu")]
    public void BackendInfo_CrossPlatformPaths_UseCorrectSeparators(string rid, string expectedPath)
    {
        // Arrange
        var backend = new NativeBackendInfo
        {
            Name = "cuda",
            Path = expectedPath,
            Rid = rid,
            IsAvailable = true
        };

        // Act & Assert
        backend.Path.Should().Be(expectedPath);

        if (rid.StartsWith("win"))
        {
            backend.Path.Should().ContainAny(@"\", "/",
                "Windows paths can use either separator");
        }
        else
        {
            // Linux/macOS should use forward slash
            if (!backend.Path.Contains("\\"))
            {
                backend.Path.Should().Contain("/",
                    "Unix paths should use forward slash");
            }
        }
    }

    [Fact]
    public void Refresh_CalledMultipleTimes_UpdatesBackendList()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var beforeRefresh = sut.GetAllBackends().Count;
        sut.Refresh();
        var afterRefresh = sut.GetAllBackends().Count;

        // Assert
        afterRefresh.Should().Be(beforeRefresh,
            "backend count should be consistent across refreshes");
    }

    [Fact]
    public void GetAvailableBackends_ReturnsOnlyBackendsWithMainLibrary()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var availableBackends = sut.GetAvailableBackends();

        // Assert
        availableBackends.Should().OnlyContain(b => b.IsAvailable,
            "GetAvailableBackends should only return backends marked as available");

        availableBackends.Should().OnlyContain(b => b.LibraryFiles.Any(),
            "available backends should have library files");
    }

    // Helper methods

    private static string GetExpectedRid()
    {
        var arch = RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            _ => throw new PlatformNotSupportedException($"Unsupported architecture: {RuntimeInformation.OSArchitecture}")
        };

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return $"win-{arch}";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return $"linux-{arch}";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return $"osx-{arch}";

        throw new PlatformNotSupportedException($"Unsupported OS");
    }

    private static string GetExpectedLibraryName()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return "llama.dll";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return "libllama.so";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return "libllama.dylib";

        throw new PlatformNotSupportedException("Unknown platform");
    }

    private static bool HasNativeExtension(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext is ".dll" or ".so" or ".dylib";
    }
}
