namespace InstantAIGate.Core.Tests.Inference;

using InstantAIGate.Core.Tests.TestConfiguration;
using InstantAIGate.Native.Bindings;
using InstantAIGate.Native.Inference;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

public class NativeModelValidatorIntegrationTests : IDisposable
{
    private readonly NativeModelValidator _validator;
    private readonly string _tempDirectory;
    private readonly TestModelOptions _modelOptions;

    public NativeModelValidatorIntegrationTests()
    {
        _validator = new NativeModelValidator(new NullLogger<NativeModelValidator>());
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"InstantAIGate_Tests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDirectory);

        if (!NativeLibraryLoader.IsLoaded)
        {
            NativeLibraryLoader.Load();
        }

        // All model parameters are read from the test project appsettings.json
        // (section \"InstantAIGate:TestData\"), no hardcoded names or paths.
        _modelOptions = TestConfig.Model;
    }



    [Fact]
    public async Task ValidateIntegrityAsync_WithEmptyFile_ReturnsFalse()
    {
        // Arrange: Create a 0-byte file (definitely an invalid GGUF)
        string filePath = Path.Combine(_tempDirectory, "empty_model.gguf");
        await File.WriteAllBytesAsync(filePath, Array.Empty<byte>());

        // Act
        bool isValid = await _validator.ValidateIntegrityAsync(new[] { filePath });

        // Assert
        Assert.False(isValid, "Native engine should reject an empty file.");
    }

    [Fact]
    public async Task ValidateIntegrityAsync_WithCorruptedHeader_ReturnsFalse()
    {
        // Arrange: Create a file with fake/invalid magic bytes instead of "GGUF"
        string filePath = Path.Combine(_tempDirectory, "corrupted_model.gguf");
        byte[] fakeData = System.Text.Encoding.UTF8.GetBytes("FAKE_HEADER_DATA_NOT_A_GGUF");
        await File.WriteAllBytesAsync(filePath, fakeData);

        // Act
        bool isValid = await _validator.ValidateIntegrityAsync(new[] { filePath });

        // Assert
        Assert.False(isValid, "Native engine should reject a file with invalid magic bytes.");
    }

    [Fact]
    public async Task ValidateIntegrityAsync_MissingFile_ReturnsFalse()
    {
        // Arrange
        string filePath = Path.Combine(_tempDirectory, "non_existent.gguf");

        // Act
        bool isValid = await _validator.ValidateIntegrityAsync(new[] { filePath });

        // Assert
        Assert.False(isValid, "Validation must fail gracefully if the file is missing.");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            try
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
            catch
            {
                // Ignore cleanup errors in test runner
            }
        }
    }


    [Fact]
    public async Task ValidateIntegrityAsync_WithRealGguf_ReturnsTrue()
    {
        // Model catalog directory and GGUF file name come from the test configuration
        // (section \"InstantAIGate:TestData\" in appsettings.json), targeting the lightweight model.
        string realModelPath = Path.Combine(_modelOptions.ModelsDirectory, _modelOptions.RepoId, _modelOptions.GgufFileName);

        // The test fails when the real model file is absent so it never passes silently
        if (!File.Exists(realModelPath))
        {
            Assert.Fail($"FATAL: Real GGUF model not found for validation test. Expected path: '{realModelPath}'");
        }

        bool isValid = await _validator.ValidateIntegrityAsync(new[] { realModelPath });

        Assert.True(isValid, "Native engine should accept a physically valid GGUF file.");
    }
}