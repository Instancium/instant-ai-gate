namespace InstantAIGate.Core.Tests.Downloader;

using InstantAIGate.Core.Tests.Inference.Stubs;
using InstantAIGate.Core.Tests.Infrastructure;
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
    public async Task E2E_CatalogToDownload_Pipeline_Succeeds()
    {
        // 1. ARRANGE: Fake Catalog
        var fakeCatalog = @"[
            {
                ""Id"": ""integration-test-model"",
                ""Name"": ""Synthetic Model"",
                ""DownloadUrls"": [""http://synth/model.gguf""]
            }
        ]";
        await File.WriteAllTextAsync(_catalogFilePath, fakeCatalog);

        // 2. ARRANGE: Services Initialization (Mimicking DI Container)
        IModelCatalogService catalogService = new ModelCatalogService(new NullLogger<ModelCatalogService>());

        var httpHandler = new SyntheticNetworkHandler(virtualFileSizeBytes: 1024 * 1024 * 5, supportRanges: true);
        var httpClient = new HttpClient(httpHandler);

        // Using the StubModelValidator we created earlier so we don't trigger native P/Invoke on a fake file
        var stubValidator = new StubModelValidator { ShouldPass = true };

        IModelDownloader downloader = new ParallelModelDownloader(
            httpClient,
            new NullLogger<ParallelModelDownloader>(),
            stubValidator);

        var progressList = new List<DownloadProgress>();
        var progress = new Progress<DownloadProgress>(progressList.Add);

        // 3. ACT: The "CLI" Workflow

        // Step A: User asks for a model from catalog
        var targetModel = await catalogService.FindModelByIdAsync("integration-test-model");
        Assert.NotNull(targetModel); // Pipeline check 1: Catalog works

        // Step B: User starts download using URLs from catalog
        await downloader.DownloadModelAsync(
            targetModel.Id,
            targetModel.DownloadUrls,
            _tempOutputDir,
            progress,
            CancellationToken.None);

        // 4. ASSERT: The final state
        Assert.NotEmpty(progressList);
        Assert.Equal(100f, progressList.Last().Percentage);

        string expectedFile = Path.Combine(_tempOutputDir, "model.gguf");
        Assert.True(File.Exists(expectedFile), "The downloaded file should exist in the target directory.");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempOutputDir)) Directory.Delete(_tempOutputDir, true);
        if (File.Exists(_catalogFilePath)) File.Delete(_catalogFilePath);
    }
}