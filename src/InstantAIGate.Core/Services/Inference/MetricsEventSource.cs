namespace InstantAIGate.Core.Services.Inference;

using InstantAIGate.Core.Interfaces.Inference;
using System.Threading.Channels;

/// <summary>
/// Thread-safe event source that coalesces rapid state changes using a single-element bounded channel.
/// </summary>
public sealed class MetricsEventSource : IMetricsEventSource
{
    private readonly Channel<bool> _channel = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
        SingleWriter = false
    });

    public ChannelReader<bool> Reader => _channel.Reader;

    public void NotifyStateChanged()
    {
        _channel.Writer.TryWrite(true);
    }
}