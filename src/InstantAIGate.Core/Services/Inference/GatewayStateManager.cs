namespace InstantAIGate.Core.Services.Inference;

using InstantAIGate.Core.Dtos.Status;
using InstantAIGate.Core.Interfaces.Inference;
using System;

/// <summary>
/// Thread-safe singleton coordinator managing gateway lifecycle transitions and diagnostics broadcast.
/// </summary>
public sealed class GatewayStateManager : IGatewayStateManager
{
    private readonly object _syncLock = new();
    private readonly TimeProvider _timeProvider;
    private GatewayStatusDetails _currentStatus;

    public event Action<GatewayStatusDetails>? StatusChanged;

    public GatewayStateManager(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _currentStatus = new GatewayStatusDetails
        {
            Status = GatewayOperationalStatus.Uninitialized,
            Timestamp = _timeProvider.GetUtcNow()
        };
    }

    public GatewayStatusDetails GetSnapshot()
    {
        lock (_syncLock)
        {
            return _currentStatus;
        }
    }

    public void SetDownloading(
        string modelId,
        float progressPercentage,
        long downloadedBytes,
        long totalBytes,
        double bytesPerSecond,
        string? stageDescription = null)
    {
        UpdateAndPublish(new GatewayStatusDetails
        {
            Status = GatewayOperationalStatus.ModelDownloading,
            ActiveModelId = modelId,
            StageDescription = stageDescription ?? $"Downloading model '{modelId}'",
            ProgressPercentage = Math.Clamp(progressPercentage, 0.0f, 100.0f),
            DownloadedBytes = downloadedBytes,
            TotalBytes = totalBytes,
            BytesPerSecond = Math.Max(0.0, bytesPerSecond),
            ErrorMessage = null,
            Timestamp = _timeProvider.GetUtcNow()
        });
    }

    public void SetLoading(string modelId, string? stageDescription = null)
    {
        UpdateAndPublish(new GatewayStatusDetails
        {
            Status = GatewayOperationalStatus.ModelLoading,
            ActiveModelId = modelId,
            StageDescription = stageDescription ?? $"Loading model '{modelId}' into memory",
            ProgressPercentage = 100.0f,
            DownloadedBytes = 0,
            TotalBytes = 0,
            BytesPerSecond = 0.0,
            ErrorMessage = null,
            Timestamp = _timeProvider.GetUtcNow()
        });
    }

    public void SetReady(string modelId, string? stageDescription = null)
    {
        UpdateAndPublish(new GatewayStatusDetails
        {
            Status = GatewayOperationalStatus.Ready,
            ActiveModelId = modelId,
            StageDescription = stageDescription ?? $"Model '{modelId}' is ready for inference",
            ProgressPercentage = 100.0f,
            DownloadedBytes = 0,
            TotalBytes = 0,
            BytesPerSecond = 0.0,
            ErrorMessage = null,
            Timestamp = _timeProvider.GetUtcNow()
        });
    }

    public void SetFaulted(string? modelId, string errorMessage, Exception? exception = null)
    {
        string fullMessage = exception != null
            ? $"{errorMessage}: {exception.Message}"
            : errorMessage;

        UpdateAndPublish(new GatewayStatusDetails
        {
            Status = GatewayOperationalStatus.Faulted,
            ActiveModelId = modelId,
            StageDescription = "Faulted",
            ProgressPercentage = 0.0f,
            DownloadedBytes = 0,
            TotalBytes = 0,
            BytesPerSecond = 0.0,
            ErrorMessage = fullMessage,
            Timestamp = _timeProvider.GetUtcNow()
        });
    }

    public void Reset()
    {
        UpdateAndPublish(new GatewayStatusDetails
        {
            Status = GatewayOperationalStatus.Uninitialized,
            ActiveModelId = null,
            StageDescription = null,
            ProgressPercentage = 0.0f,
            DownloadedBytes = 0,
            TotalBytes = 0,
            BytesPerSecond = 0.0,
            ErrorMessage = null,
            Timestamp = _timeProvider.GetUtcNow()
        });
    }

    private void UpdateAndPublish(GatewayStatusDetails newStatus)
    {
        Action<GatewayStatusDetails>? handler;
        lock (_syncLock)
        {
            _currentStatus = newStatus;
            handler = StatusChanged;
        }

        if (handler == null)
        {
            return;
        }

        // Invoke outside the lock to prevent deadlocks and isolate faulty subscribers
        foreach (var callback in handler.GetInvocationList())
        {
            try
            {
                ((Action<GatewayStatusDetails>)callback)(newStatus);
            }
            catch
            {
                // Fault-isolation: a broken observer cannot disrupt the core state machine
            }
        }
    }
}