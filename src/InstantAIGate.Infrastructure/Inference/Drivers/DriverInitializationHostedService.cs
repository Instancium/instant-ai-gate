using InstantAIGate.Application.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace InstantAIGate.Infrastructure.Inference.Drivers;

public sealed class DriverInitializationHostedService : IHostedService
{
    private readonly IDriverStateProvider _stateProvider;
    private readonly ILogger<DriverInitializationHostedService> _logger;

    public DriverInitializationHostedService(
        IDriverStateProvider stateProvider,
        ILogger<DriverInitializationHostedService> logger)
    {
        _stateProvider = stateProvider ?? throw new ArgumentNullException(nameof(stateProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _stateProvider.BeginExtraction();

        DriverNativeResolver.Attach();

        _logger.LogInformation("Native driver extraction detached and starting.");

        _ = Task.Run(() =>
        {
            try
            {
                LlamaDriverLoader.EnsureInitialized();
                _logger.LogInformation("Native drivers initialized and mapped successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "Critical failure during native driver extraction.");
            }
            finally
            {
                _stateProvider.EndExtraction();
            }
        }, CancellationToken.None);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}