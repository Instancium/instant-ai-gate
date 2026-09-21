namespace InstantAIGate.Core.Tests.Inference;

using InstantAIGate.Native.Inference;
using InstantAIGate.Native.Bindings;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

public class NativeModelValidatorIntegrationTests : IDisposable
{
    private readonly NativeModelValidator _validator;
    private readonly string _tempDirectory;

    public NativeModelValidatorIntegrationTests()
    {
        _validator = new NativeModelValidator(new NullLogger<NativeModelValidator>());
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"InstantAIGate_Tests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDirectory);

        // Ensure native libraries are loaded for P/Invoke bindings
        if (!NativeLibraryLoader.IsLoaded)
        {
            NativeLibraryLoader.Load();
        }
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
}