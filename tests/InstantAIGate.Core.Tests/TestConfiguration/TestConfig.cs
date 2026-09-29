using Microsoft.Extensions.Configuration;

namespace InstantAIGate.Core.Tests.TestConfiguration;

/// <summary>
/// Single entry point for loading the test project configuration
/// (appsettings.json + environment variables).
/// Used by all tests instead of hardcoded values.
/// </summary>
public static class TestConfig
{
    private static readonly Lazy<IConfiguration> LazyConfiguration = new(() =>
        new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build());

    /// <summary>Test application configuration.</summary>
    public static IConfiguration Configuration => LazyConfiguration.Value;

    /// <summary>Test model options ("InstantAIGate:TestData" section).</summary>
    public static TestModelOptions Model { get; } = LoadModelOptions();

    /// <summary>Test server addresses and secrets ("InstantAIGate:TestServer" section).</summary>
    public static TestServerOptions Server { get; } = LoadServerOptions();

    private static TestModelOptions LoadModelOptions()
    {
        var options = new TestModelOptions();
        Configuration.GetSection(TestModelOptions.SectionName).Bind(options);

        // Fallback to the legacy VisionRepoId key if the new section is not set explicitly.
        var legacyRepoId = Configuration["InstantAIGate:TestData:VisionRepoId"];
        if (!string.IsNullOrWhiteSpace(legacyRepoId))
        {
            return new TestModelOptions
            {
                RepoId = legacyRepoId,
                GgufFileName = options.GgufFileName,
                ModelsDirectory = options.ModelsDirectory,
                ModelLoad = options.ModelLoad,
                Inference = options.Inference,
                SyntheticDownload = options.SyntheticDownload
            };
        }

        return options;
    }

    private static TestServerOptions LoadServerOptions()
    {
        var options = new TestServerOptions();
        Configuration.GetSection(TestServerOptions.SectionName).Bind(options);
        return options;
    }
}
