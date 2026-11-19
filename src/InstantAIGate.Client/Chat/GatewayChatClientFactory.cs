namespace InstantAIGate.Client.Chat;

using System;
using InstantAIGate.Client.Configuration;
using InstantAIGate.Client.Context;
using InstantAIGate.Client.Transport;
using Microsoft.Extensions.Logging;

/// <summary>
/// Default <see cref="IChatClientFactory"/> wiring the shared <see cref="IGatewayConnection"/>
/// with either a built-in <see cref="ChatContextPolicy"/> or a caller-supplied <see cref="IChatContextStrategy"/>.
/// </summary>
public sealed class GatewayChatClientFactory : IChatClientFactory
{
    private readonly IGatewayConnection _connection;
    private readonly ILoggerFactory _loggerFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="GatewayChatClientFactory"/> class.
    /// </summary>
    /// <param name="connection">The shared gateway SignalR connection.</param>
    /// <param name="loggerFactory">The logger factory used to create loggers for chat clients.</param>
    public GatewayChatClientFactory(IGatewayConnection connection, ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _connection = connection;
        _loggerFactory = loggerFactory;
    }

    /// <inheritdoc />
    public IChatClient Create(ChatContextPolicy policy)
    {
        IChatContextStrategy strategy = policy switch
        {
            ChatContextPolicy.Stateless => new StatelessChatContextStrategy(),
            ChatContextPolicy.Conversational => new ConversationalChatContextStrategy(),
            _ => throw new ArgumentOutOfRangeException(nameof(policy), policy, "Unsupported chat context policy.")
        };

        return Create(strategy);
    }

    /// <inheritdoc />
    public IChatClient Create(IChatContextStrategy strategy)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        return new GatewayChatClient(
            _connection,
            strategy,
            _loggerFactory.CreateLogger<GatewayChatClient>());
    }
}
