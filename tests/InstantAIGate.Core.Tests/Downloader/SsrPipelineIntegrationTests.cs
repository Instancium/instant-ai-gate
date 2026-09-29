using InstantAIGate.Core.Exceptions;
using InstantAIGate.Core.Interfaces.Inference;
using InstantAIGate.Core.Tests.Infrastructure;
using InstantAIGate.Core.Tests.TestConfiguration;
using InstantAIGate.Native.Inference;
using InstantAIGate.SSR.Contracts;
using InstantAIGate.SSR.Downloader;
using InstantAIGate.SSR.Dtos;
using Microsoft.Extensions.Logging.Abstractions;

namespace InstantAIGate.Core.Tests.Downloader;

public class SsrPipelineIntegrationTests : IDisposable
{
    // Synthetic sizes and URLs come from the test project appsettings.json.
    private static readonly TestSyntheticDownloadOptions SynthOptions = TestConfig.Model.SyntheticDownload;

    private readonly string _tempOutputDir;

    public SsrPipelineIntegrationTests()
    {
        _tempOutputDir = Path.Combine(Path.GetTempPath(), $"SsrTest_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempOutputDir);
    }

    [Fact]
    public async Task E2E_Pipeline_Rejects_Corrupted_Network_Payload()
    {
        var httpHandler = new SyntheticNetworkHandler(virtualFileSizeBytes: SynthOptions.CorruptedPayloadFileSizeBytes, supportRanges: true);
        var httpClient = new HttpClient(httpHandler);

        IModelValidator realValidator = new NativeModelValidator(new NullLogger<NativeModelValidator>());
        IModelDownloader downloader = new ParallelModelDownloader(httpClient, new NullLogger<ParallelModelDownloader>(), realValidator);

        string testModelId = "integration-test";
        var downloadUrls = new List<string> { SynthOptions.BaseUrl + "model.gguf" };
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
