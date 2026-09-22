using InstantAIGate.Core.Exceptions;
using InstantAIGate.Core.Interfaces.Inference;
using InstantAIGate.Core.Tests.Infrastructure;
using InstantAIGate.Native.Inference;
using InstantAIGate.SSR.Contracts;
using InstantAIGate.SSR.Downloader;
using InstantAIGate.SSR.Dtos;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace InstantAIGate.Core.Tests.Downloader;

public class SsrPipelineIntegrationTests : IDisposable
{
    private readonly string _tempOutputDir;

    public SsrPipelineIntegrationTests()
    {
        _tempOutputDir = Path.Combine(Path.GetTempPath(), $"SsrTest_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempOutputDir);
    }

    [Fact]
    public async Task E2E_Pipeline_Rejects_Corrupted_Network_Payload()
    {
        
        var httpHandler = new SyntheticNetworkHandler(virtualFileSizeBytes: 1024 * 1024 * 5, supportRanges: true);
        var httpClient = new HttpClient(httpHandler);

        IModelValidator realValidator = new NativeModelValidator(new NullLogger<NativeModelValidator>());
        IModelDownloader downloader = new ParallelModelDownloader(httpClient, new NullLogger<ParallelModelDownloader>(), realValidator);

   
        string testModelId = "integration-test";
        var downloadUrls = new List<string> { "http://synth/model.gguf" };
        var progress = new Progress<DownloadProgress>();

        await Assert.ThrowsAsync<ModelIntegrityException>(async () =>
        {
            await downloader.DownloadModelAsync(
                testModelId,
                downloadUrls,
                _tempOutputDir,
                progress,
                CancellationToken.None);
        });
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempOutputDir))
        {
            Directory.Delete(_tempOutputDir, true);
        }
    }
}