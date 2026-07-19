using InstantAIGate.Application.Dtos.Inference;
using InstantAIGate.Application.Interfaces.Inference;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace InstantAIGate.Infrastructure.Inference
{
    /// <summary>
    /// Background service that consumes queued requests and dispatches them to the single active model.
    /// </summary>
    public class InferenceWorker : BackgroundService
    {
        private readonly RequestQueue _queue;
        private readonly ModelManager _modelManager;
        private readonly ILogger<InferenceWorker> _logger;

        public InferenceWorker(
            RequestQueue queue,
            ModelManager modelManager,
            ILogger<InferenceWorker> logger)
        {
            _queue = queue;
            _modelManager = modelManager;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("InferenceWorker started processing request queue.");

            while (!stoppingToken.IsCancellationRequested)
            {
                PendingInferenceRequest? request = null;

                try
                {
                    request = await _queue.DequeueAsync(stoppingToken);

                    if (request.CancellationToken.IsCancellationRequested)
                    {
                        request.CompletionSource.TrySetCanceled(request.CancellationToken);
                        continue;
                    }

                    var context = await _modelManager.AcquireContextAsync(request.RepoId, stoppingToken);
                    request.CompletionSource.TrySetResult(context);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("InferenceWorker cancellation requested. Shutting down.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to process inference request for model '{RepoId}'.", request?.RepoId);
                    request?.CompletionSource.TrySetException(ex);
                }
            }
        }
    }
}