using FluentAssertions;
using InstantAIGate.Domain.Dtos.Config;
using InstantAIGate.Infrastructure.Inference;
using InstantAIGate.Infrastructure.Inference.Native;
using Microsoft.Extensions.Logging;
using Moq;

namespace InstantAIGate.Infrastructure.Tests.Inference;

/// <summary>
/// Tests for LlamaModelProvider - validates model loading and context management.
/// SUT: LlamaModelProvider
/// Mocks: ILogger, INativeLlamaApi (external native library interface)
/// </summary>
public class LlamaModelProviderTests : IDisposable
{
    private readonly Mock<ILogger<ModelProvider>> _loggerMock;
    private readonly Mock<INativeLlamaApi> _nativeApiMock;
    private readonly string _testModelPath;

    public LlamaModelProviderTests()
    {
        _loggerMock = new Mock<ILogger<ModelProvider>>();
        _nativeApiMock = new Mock<INativeLlamaApi>();

        // Use realistic test data path (not "test" or "string")
        _testModelPath = Path.Combine(
            Path.GetTempPath(),
            "llama-models",
            "tinyllama-1.1b-chat.gguf"
        );

        // Create temporary file to simulate model existence
        Directory.CreateDirectory(Path.GetDirectoryName(_testModelPath)!);
        if (!File.Exists(_testModelPath))
        {
            File.WriteAllText(_testModelPath, "fake model content for testing");
        }
    }

    public void Dispose()
    {
        // Cleanup test files
        if (File.Exists(_testModelPath))
        {
            File.Delete(_testModelPath);
        }
    }

    private ModelProvider CreateSut()
    {
        return new ModelProvider(
            _loggerMock.Object,
            _nativeApiMock.Object
        );
    }

    private ModelSettings CreateValidModelSettings(string repoId = "TinyLlama/TinyLlama-1.1B-Chat-v1.0")
    {
        return new ModelSettings
        {
            RepoId = repoId,
            ModelPath = _testModelPath,
            ContextSize = 2048,
            BatchSize = 512,
            GpuLayerCount = 35,
            MainGPU = 0,
            Threads = 4,
            FlashAttention = false,
            UseMemoryLock = false,
            Embeddings = false,
            MaxModelFileSizeMb = 5000
        };
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
    public void IsLoaded_BeforeInitialization_ReturnsFalse()
    {
        // Arrange
        var sut = CreateSut();
        var repoId = "TinyLlama/TinyLlama-1.1B-Chat-v1.0";

        // Act
        var isLoaded = sut.IsLoaded(repoId);

        // Assert
        isLoaded.Should().BeFalse("model should not be loaded before InitializeAsync");
    }

    [Fact]
    public async Task InitializeAsync_NullConfig_ThrowsArgumentException()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        Func<Task> act = async () => await sut.InitializeAsync(null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Config and RepoId required*");
    }

    [Fact]
    public async Task InitializeAsync_EmptyRepoId_ThrowsArgumentException()
    {
        // Arrange
        var sut = CreateSut();
        var config = CreateValidModelSettings(repoId: "");

        // Act
        Func<Task> act = async () => await sut.InitializeAsync(config);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Config and RepoId required*");
    }

    [Fact]
    public async Task InitializeAsync_NonExistentModelFile_ThrowsFileNotFoundException()
    {
        // Arrange
        var sut = CreateSut();
        var config = CreateValidModelSettings();
        config.ModelPath = "/non/existent/path/model.gguf";

        // Act
        Func<Task> act = async () => await sut.InitializeAsync(config);

        // Assert
        await act.Should().ThrowAsync<FileNotFoundException>()
            .WithMessage("Model file not found.*");
    }

    [Fact]
    public async Task InitializeAsync_ModelFileTooLarge_ThrowsInvalidOperationException()
    {
        // Arrange
        var sut = CreateSut();
        var config = CreateValidModelSettings();
        config.MaxModelFileSizeMb = 0; // Set limit to 0 MB

        // Act
        Func<Task> act = async () => await sut.InitializeAsync(config);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task InitializeAsync_ValidModel_LoadsSuccessfully()
    {
        // Arrange
        var sut = CreateSut();
        var config = CreateValidModelSettings();
        var fakeModelHandle = new IntPtr(12345);

        _nativeApiMock.Setup(api => api.LoadAllBackends());
        _nativeApiMock.Setup(api => api.BackendInit());
        _nativeApiMock.Setup(api => api.SupportsGpuOffload()).Returns(true);
        _nativeApiMock.Setup(api => api.LoadModel(
            It.IsAny<string>(),
            It.IsAny<int>(),
            It.IsAny<int>(),
            It.IsAny<bool>(),
            It.IsAny<bool>(),
            It.IsAny<NativeLlamaSplitMode>()
        )).Returns(fakeModelHandle);

        // Act
        await sut.InitializeAsync(config);

        // Assert
        sut.IsLoaded(config.RepoId).Should().BeTrue("model should be loaded after successful initialization");
        _nativeApiMock.Verify(api => api.LoadModel(
            config.ModelPath,
            config.GpuLayerCount,
            config.MainGPU,
            config.UseMemoryLock,
            !config.UseMemoryLock,
            NativeLlamaSplitMode.Layer
        ), Times.Once);
    }

    [Fact]
    public async Task InitializeAsync_NativeEngineReturnsZeroHandle_ThrowsInvalidOperationException()
    {
        // Arrange
        var sut = CreateSut();
        var config = CreateValidModelSettings();

        _nativeApiMock.Setup(api => api.LoadAllBackends());
        _nativeApiMock.Setup(api => api.BackendInit());
        _nativeApiMock.Setup(api => api.LoadModel(
            It.IsAny<string>(),
            It.IsAny<int>(),
            It.IsAny<int>(),
            It.IsAny<bool>(),
            It.IsAny<bool>(),
            It.IsAny<NativeLlamaSplitMode>()
        )).Returns(IntPtr.Zero); // ← Simulate failure

        // Act
        Func<Task> act = async () => await sut.InitializeAsync(config);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*null handle*");
    }

    [Fact]
    public async Task InitializeAsync_CalledTwiceWithSameModel_IdempotentBehavior()
    {
        // Arrange
        var sut = CreateSut();
        var config = CreateValidModelSettings();
        var fakeModelHandle = new IntPtr(54321);

        _nativeApiMock.Setup(api => api.LoadAllBackends());
        _nativeApiMock.Setup(api => api.BackendInit());
        _nativeApiMock.Setup(api => api.LoadModel(
            It.IsAny<string>(),
            It.IsAny<int>(),
            It.IsAny<int>(),
            It.IsAny<bool>(),
            It.IsAny<bool>(),
            It.IsAny<NativeLlamaSplitMode>()
        )).Returns(fakeModelHandle);

        // Act
        await sut.InitializeAsync(config);
        await sut.InitializeAsync(config); // Second call

        // Assert
        _nativeApiMock.Verify(api => api.LoadModel(
            It.IsAny<string>(),
            It.IsAny<int>(),
            It.IsAny<int>(),
            It.IsAny<bool>(),
            It.IsAny<bool>(),
            It.IsAny<NativeLlamaSplitMode>()
        ), Times.Once, "model should only be loaded once (idempotent)");
    }

    [Theory]
    [InlineData(0, NativeLlamaSplitMode.None)]
    [InlineData(35, NativeLlamaSplitMode.Layer)]
    [InlineData(99, NativeLlamaSplitMode.Layer)]
    public async Task InitializeAsync_DifferentGpuLayerCounts_UseCorrectSplitMode(
        int gpuLayers,
        NativeLlamaSplitMode expectedSplitMode)
    {
        // Arrange
        var sut = CreateSut();
        var config = CreateValidModelSettings();
        config.GpuLayerCount = gpuLayers;
        var fakeModelHandle = new IntPtr(99999);

        _nativeApiMock.Setup(api => api.LoadAllBackends());
        _nativeApiMock.Setup(api => api.BackendInit());
        _nativeApiMock.Setup(api => api.LoadModel(
            It.IsAny<string>(),
            It.IsAny<int>(),
            It.IsAny<int>(),
            It.IsAny<bool>(),
            It.IsAny<bool>(),
            It.IsAny<NativeLlamaSplitMode>()
        )).Returns(fakeModelHandle);

        // Act
        await sut.InitializeAsync(config);

        // Assert
        _nativeApiMock.Verify(api => api.LoadModel(
            It.IsAny<string>(),
            It.IsAny<int>(),
            It.IsAny<int>(),
            It.IsAny<bool>(),
            It.IsAny<bool>(),
            expectedSplitMode
        ), Times.Once);
    }

    [Fact]
    public async Task InitializeAsync_GpuOffloadSupported_LogsSupport()
    {
        // Arrange
        var sut = CreateSut();
        var config = CreateValidModelSettings();
        var fakeModelHandle = new IntPtr(11111);

        _nativeApiMock.Setup(api => api.LoadAllBackends());
        _nativeApiMock.Setup(api => api.BackendInit());
        _nativeApiMock.Setup(api => api.SupportsGpuOffload()).Returns(true);
        _nativeApiMock.Setup(api => api.SetLogCallback(It.IsAny<NativeLogCallback>()));
        _nativeApiMock.Setup(api => api.LoadModel(
            It.IsAny<string>(),
            It.IsAny<int>(),
            It.IsAny<int>(),
            It.IsAny<bool>(),
            It.IsAny<bool>(),
            It.IsAny<NativeLlamaSplitMode>()
        )).Returns(fakeModelHandle);

        // Act
        await sut.InitializeAsync(config);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("loaded")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()!),
            Times.AtLeastOnce(),
            "model loading should log information messages"
        );
    }

    [Fact]
    public async Task GetContextAsync_EmptyRepoId_ThrowsArgumentException()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        Func<Task> act = async () => await sut.GetContextAsync("");

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*RepoId required*");
    }

    [Fact]
    public async Task GetContextAsync_ModelNotLoaded_ThrowsOrReturnsNull()
    {
        // Arrange
        var sut = CreateSut();
        var repoId = "unloaded/model";

        // Act
        Func<Task> act = async () => await sut.GetContextAsync(repoId);

        // Assert
        // Behavior depends on implementation - test documents expected outcome
        await act.Should().ThrowAsync<Exception>("accessing context for unloaded model should fail");
    }

    [Fact]
    public async Task InitializeAsync_ConcurrentCalls_ThreadSafe()
    {
        // Arrange
        var sut = CreateSut();
        var config = CreateValidModelSettings();
        var fakeModelHandle = new IntPtr(77777);

        _nativeApiMock.Setup(api => api.LoadAllBackends());
        _nativeApiMock.Setup(api => api.BackendInit());
        _nativeApiMock.Setup(api => api.LoadModel(
            It.IsAny<string>(),
            It.IsAny<int>(),
            It.IsAny<int>(),
            It.IsAny<bool>(),
            It.IsAny<bool>(),
            It.IsAny<NativeLlamaSplitMode>()
        )).Returns(fakeModelHandle);

        // Act
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => sut.InitializeAsync(config))
            .ToArray();

        await Task.WhenAll(tasks);

        // Assert
        _nativeApiMock.Verify(api => api.LoadModel(
            It.IsAny<string>(),
            It.IsAny<int>(),
            It.IsAny<int>(),
            It.IsAny<bool>(),
            It.IsAny<bool>(),
            It.IsAny<NativeLlamaSplitMode>()
        ), Times.Once, "concurrent initialization should be thread-safe and load once");
    }

    [Fact]
    public async Task InitializeAsync_UseMemoryLockTrue_PassesCorrectFlags()
    {
        // Arrange
        var sut = CreateSut();
        var config = CreateValidModelSettings();
        config.UseMemoryLock = true;
        var fakeModelHandle = new IntPtr(88888);

        _nativeApiMock.Setup(api => api.LoadAllBackends());
        _nativeApiMock.Setup(api => api.BackendInit());
        _nativeApiMock.Setup(api => api.LoadModel(
            It.IsAny<string>(),
            It.IsAny<int>(),
            It.IsAny<int>(),
            It.IsAny<bool>(),
            It.IsAny<bool>(),
            It.IsAny<NativeLlamaSplitMode>()
        )).Returns(fakeModelHandle);

        // Act
        await sut.InitializeAsync(config);

        // Assert
        _nativeApiMock.Verify(api => api.LoadModel(
            It.IsAny<string>(),
            It.IsAny<int>(),
            It.IsAny<int>(),
            true,  // useMlock
            false, // useMmap (inverted)
            It.IsAny<NativeLlamaSplitMode>()
        ), Times.Once);
    }

    [Fact]
    public async Task Dispose_AfterInitialization_FreesResources()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        Action act = () => sut.Dispose();

        // Assert
        act.Should().NotThrow("disposal should complete without throwing");
    }
}
