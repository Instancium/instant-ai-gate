namespace InstantAIGate.Core.Tests.Downloader;

using InstantAIGate.Core.Exceptions;
using InstantAIGate.Core.Interfaces.Inference;
using InstantAIGate.Core.Tests.Inference.Stubs;
using InstantAIGate.Core.Tests.Infrastructure;
using InstantAIGate.Native.Inference;
using InstantAIGate.SSR.Contracts;
using InstantAIGate.SSR.Downloader;
using InstantAIGate.SSR.Dtos;
using InstantAIGate.SSR.Services;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

public class SsrPipelineIntegrationTests : IDisposable
{
    private readonly string _tempOutputDir;
    private readonly string _testConfigDir;
    private readonly string _catalogFilePath;

    public SsrPipelineIntegrationTests()
    {
        _tempOutputDir = Path.Combine(Path.GetTempPath(), $"SsrTest_{Guid.NewGuid()}");
        _testConfigDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config");
        Directory.CreateDirectory(_tempOutputDir);
        Directory.CreateDirectory(_testConfigDir);
        _catalogFilePath = Path.Combine(_testConfigDir, "model_catalog.json");
    }

    [Fact]
    public async Task E2E_Pipeline_Rejects_Corrupted_Network_Payload()
    {
        // 1. Arrange: Fake Catalog & Synthetic Network (yielding fake bytes)
        var fakeCatalog = @"[ { ""Id"": ""integration-test"", ""DownloadUrls"": [""http://synth/model.gguf""] } ]";
        await File.WriteAllTextAsync(_catalogFilePath, fakeCatalog);

        IModelCatalogService catalogService = new ModelCatalogService(new NullLogger<ModelCatalogService>());
        var httpHandler = new SyntheticNetworkHandler(virtualFileSizeBytes: 1024 * 1024 * 5, supportRanges: true);
        var httpClient = new HttpClient(httpHandler);

        // 2. CRITICAL: Use REAL NativeModelValidator
        IModelValidator realValidator = new NativeModelValidator(new NullLogger<NativeModelValidator>());
        IModelDownloader downloader = new ParallelModelDownloader(
            httpClient, new NullLogger<ParallelModelDownloader>(), realValidator);

        var targetModel = await catalogService.FindModelByIdAsync("integration-test");
        var progress = new Progress<DownloadProgress>();

        // 3. Act & Assert: The downloader must THROW because the downloaded bytes lack real GGUF metadata.
        await Assert.ThrowsAsync<ModelIntegrityException>(async () =>
        {
            await downloader.DownloadModelAsync(
                targetModel!.Id, targetModel.DownloadUrls, _tempOutputDir, progress, CancellationToken.None);
        });
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempOutputDir)) Directory.Delete(_tempOutputDir, true);
        if (File.Exists(_catalogFilePath)) File.Delete(_catalogFilePath);
    }
}