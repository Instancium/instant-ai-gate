using InstantAIGate.Infrastructure.Inference.Drivers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace InstantAIGate.Infrastructure.Services
{
    /// <summary>
    /// Hosted service that automatically loads native libraries at application startup.
    /// Resolves the best backend based on configuration and availability.
    /// </summary>
    public class NativeInitializationService : IHostedService
    {
        private readonly INativeBackendRegistry _backendRegistry;
        private readonly INativeLibraryLoader _libraryLoader;
        private readonly NativeLibraryOptions _options;
        private readonly ILogger<NativeInitializationService> _logger;

        public NativeInitializationService(
            INativeBackendRegistry backendRegistry,
            INativeLibraryLoader libraryLoader,
            IOptions<NativeLibraryOptions> options,
            ILogger<NativeInitializationService> logger)
        {
            _backendRegistry = backendRegistry ?? throw new ArgumentNullException(nameof(backendRegistry));
            _libraryLoader = libraryLoader ?? throw new ArgumentNullException(nameof(libraryLoader));
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Executes initialization logic inside a background task to allow Kestrel socket allocation.
        /// </summary>
        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Initializing native library loader service.");

            _ = Task.Run(() =>
            {
                try
                {
                    _logger.LogInformation("Refreshing backend registry state asynchronously.");

                    // Explicitly call Refresh() here to keep extraction logic out of the DI lifecycle.
                    _backendRegistry.Refresh();

                    if (_options.EnableDebugLogging)
                    {
                        _logger.LogDebug("Preferred backend configuration: {Preferred}", _options.PreferredBackend);
                    }

                    var backend = _backendRegistry.ResolveBackend(_options.PreferredBackend);
                    _libraryLoader.LoadBackend(backend);

                    _logger.LogInformation(
                        "Native library initialization completed successfully. Backend: {Backend}",
                        backend.Name);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to initialize native library loader in background execution context.");
                }
            }, cancellationToken);

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Native library initialization service stopping.");
            return Task.CompletedTask;
        }
    }
}