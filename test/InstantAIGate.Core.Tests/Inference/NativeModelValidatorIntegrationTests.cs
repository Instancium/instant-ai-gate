namespace InstantAIGate.Core.Tests.Inference;

using InstantAIGate.Native.Inference;
using InstantAIGate.Native.Bindings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

public class NativeModelValidatorIntegrationTests : IDisposable
{
    private readonly NativeModelValidator _validator;
    private readonly string _tempDirectory;
    private readonly string _testModelsDir;
    private readonly string _testRepoId;

    public NativeModelValidatorIntegrationTests()
    {
        _validator = new NativeModelValidator(new NullLogger<NativeModelValidator>());
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"InstantAIGate_Tests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDirectory);

        if (!NativeLibraryLoader.IsLoaded)
        {
            NativeLibraryLoader.Load();
        }

        // Собираем конфигурацию
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        _testModelsDir = configuration["TEST_MODELS_DIR"]
            ?? configuration["InstantAIGate:Storage:ModelsDirectory"]
            ?? @"C:\models";

        _testRepoId = configuration["InstantAIGate:TestData:VisionRepoId"]
            ?? "qwen3-vl-8b-instruct";
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

    // Note: To test a TRUE case, you would need to bundle a tiny, valid 1KB dummy .gguf 
    // file in your test project resources and copy it to the temp directory.

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
        // Adjust path based on your CI/CD environment variables or local setup
        string modelsDir = Environment.GetEnvironmentVariable("TEST_MODELS_DIR") ?? @"C:\models";
        string realModelPath = Path.Combine(modelsDir, "qwen3-vl-8b-instruct", "Qwen3VL-8B-Instruct-Q4_K_M.gguf");

        // Fail fast if the environment is not prepared! No green checks for missing files.
        if (!File.Exists(realModelPath))
        {
            Assert.Fail($"FATAL: Real GGUF model not found for validation test. Expected path: '{realModelPath}'");
        }

        bool isValid = await _validator.ValidateIntegrityAsync(new[] { realModelPath });

        Assert.True(isValid, "Native engine should accept a physically valid GGUF file.");
    }
}