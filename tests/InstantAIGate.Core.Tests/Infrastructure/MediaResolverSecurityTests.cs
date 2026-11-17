namespace InstantAIGate.Core.Tests.Infrastructure;

using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Interfaces.Inference;
using InstantAIGate.Core.Services.Infrastructure;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

public sealed class MediaResolverSecurityTests : IDisposable
{
    private readonly Mock<IAssetManager> _mockAssetManager;
    private readonly LocalTempMediaResolver _resolver;
    private readonly string _tempDirectory;
    private readonly string _validImagePath;

    public MediaResolverSecurityTests()
    {
        _mockAssetManager = new Mock<IAssetManager>();
        _resolver = new LocalTempMediaResolver(_mockAssetManager.Object);
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"IAG_MediaSec_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);
        _validImagePath = Path.Combine(_tempDirectory, "test-image.jpg");
        File.WriteAllBytes(_validImagePath, new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 });
    }

    [Fact]
    public async Task ResolveMediaAsync_ValidLocalImagePath_ResolvesSuccessfully()
    {
        var parts = new List<MessageContent> { new ImageFileContent(_validImagePath) };
        var context = await _resolver.ResolveMediaAsync(parts, CancellationToken.None);
        Assert.Single(context.LocalFilePaths);
        Assert.Equal(Path.GetFullPath(_validImagePath), context.LocalFilePaths[0]);
    }

    [Fact]
    public async Task ResolveMediaAsync_PathWithDotDot_NormalizesAndResolves()
    {
        string traversalPath = Path.Combine(_tempDirectory, "..", Path.GetFileName(_tempDirectory), "test-image.jpg");
        var parts = new List<MessageContent> { new ImageFileContent(traversalPath) };
        var context = await _resolver.ResolveMediaAsync(parts, CancellationToken.None);
        Assert.Single(context.LocalFilePaths);
        Assert.Equal(Path.GetFullPath(_validImagePath), context.LocalFilePaths[0]);
    }

    [Theory]
    [InlineData(".txt")]
    [InlineData(".exe")]
    [InlineData(".dll")]
    [InlineData(".sh")]
    [InlineData(".ps1")]
    [InlineData(".json")]
    [InlineData("")]
    public async Task ResolveMediaAsync_NonImageExtension_ThrowsUnauthorizedAccess(string extension)
    {
        string badPath = Path.Combine(_tempDirectory, $"malicious{extension}");
        File.WriteAllText(badPath, "test");
        var parts = new List<MessageContent> { new ImageFileContent(badPath) };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _resolver.ResolveMediaAsync(parts, CancellationToken.None));
    }

    [Fact]
    public async Task ResolveMediaAsync_NullByteInPath_ThrowsArgumentException()
    {
        var parts = new List<MessageContent> { new ImageFileContent("/etc/passwd\0.jpg") };
        await Assert.ThrowsAsync<ArgumentException>(
            () => _resolver.ResolveMediaAsync(parts, CancellationToken.None));
    }

    [Fact]
    public async Task ResolveMediaAsync_EmptyPath_ThrowsArgumentException()
    {
        var parts = new List<MessageContent> { new ImageFileContent("") };
        await Assert.ThrowsAsync<ArgumentException>(
            () => _resolver.ResolveMediaAsync(parts, CancellationToken.None));
    }

    [Fact]
    public async Task ResolveMediaAsync_NonExistentImageFile_ThrowsFileNotFound()
    {
        string missingPath = Path.Combine(_tempDirectory, "nonexistent.png");
        var parts = new List<MessageContent> { new ImageFileContent(missingPath) };
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => _resolver.ResolveMediaAsync(parts, CancellationToken.None));
    }

    [Fact]
    public async Task ResolveMediaAsync_SystemFilePath_ThrowsUnauthorizedAccess()
    {
        string systemPath = OperatingSystem.IsWindows()
            ? @"C:\Windows\System32\cmd.exe"
            : "/etc/passwd";
        var parts = new List<MessageContent> { new ImageFileContent(systemPath) };
        await Assert.ThrowsAnyAsync<Exception>(
            () => _resolver.ResolveMediaAsync(parts, CancellationToken.None));
    }

    [Theory]
    [InlineData(".png")]
    [InlineData(".bmp")]
    [InlineData(".webp")]
    [InlineData(".gif")]
    public async Task ResolveMediaAsync_AllAllowedImageExtensions_ResolveSuccessfully(string extension)
    {
        string imagePath = Path.Combine(_tempDirectory, $"valid{extension}");
        File.WriteAllBytes(imagePath, new byte[] { 0x00 });
        var parts = new List<MessageContent> { new ImageFileContent(imagePath) };
        var context = await _resolver.ResolveMediaAsync(parts, CancellationToken.None);
        Assert.Single(context.LocalFilePaths);
    }

    [Fact]
    public async Task ResolveMediaAsync_Base64Content_DelegatesToAssetManager()
    {
        _mockAssetManager
            .Setup(a => a.GetOrCacheMediaAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("/tmp/cached_asset.bin");
        var parts = new List<MessageContent> { new ImageBase64Content("dGVzdA==", "image/png") };
        var context = await _resolver.ResolveMediaAsync(parts, CancellationToken.None);
        Assert.Single(context.LocalFilePaths);
        Assert.Equal("/tmp/cached_asset.bin", context.LocalFilePaths[0]);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            try { Directory.Delete(_tempDirectory, recursive: true); } catch { }
        }
    }
}
