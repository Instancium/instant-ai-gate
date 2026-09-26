using InstantAIGate.Server.Hubs;
using Microsoft.AspNetCore.SignalR;
using System.Threading.Channels;

namespace InstantAIGate.Server.Diagnostics;

public class SignalRLoggerProvider : ILoggerProvider, IHostedService
{
    private readonly Channel<LogMessage> _logChannel;
    private IHubContext<TelemetryHub, ITelemetryClient>? _hubContext;

    public SignalRLoggerProvider()
    {
        // Unbounded channel for high-throughput native logs, or bounded if memory constraints apply.
        _logChannel = Channel.CreateBounded<LogMessage>(new BoundedChannelOptions(5000)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });
    }

    public void SetHubContext(IHubContext<TelemetryHub, ITelemetryClient> hubContext)
    {
        _hubContext = hubContext;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new SignalRLogger(categoryName, _logChannel.Writer);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _ = Task.Run(async () =>
        {
            await foreach (var log in _logChannel.Reader.ReadAllAsync(cancellationToken))
            {
                if (_hubContext != null)
                {
                    try
                    {
                        await _hubContext.Clients.All.ReceiveLog(log.Level, log.Category, log.Message);
                    }
                    catch { /* Ignore SignalR dispatch exceptions */ }
                }
            }
        }, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public void Dispose() { }
}

public class SignalRLogger : ILogger
{
    private readonly string _categoryName;
    private readonly ChannelWriter<LogMessage> _writer;

    public SignalRLogger(string categoryName, ChannelWriter<LogMessage> writer)
    {
        _categoryName = categoryName;
        _writer = writer;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        string message = formatter(state, exception);
        if (exception != null) message += $"\n{exception}";

        _writer.TryWrite(new LogMessage(logLevel.ToString(), _categoryName, message));
    }
}

public record LogMessage(string Level, string Category, string Message);