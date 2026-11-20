namespace InstantAIGate.Client.Chat;

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using InstantAIGate.Core.Dtos.Inference;

/// <summary>
/// Convenience extension methods for <see cref="IChatClient"/> allowing plain string prompts
/// and pull-based <see cref="IAsyncEnumerable{T}"/> streaming without requiring callers to
/// construct <see cref="ChatMessage"/> wrappers or hand-write channel pumps.
/// </summary>
public static class ChatClientExtensions
{
    /// <summary>
    /// Wraps a plain text user prompt in a <see cref="ChatMessage"/> and buffers the full assistant response.
    /// </summary>
    /// <param name="client">The chat client instance.</param>
    /// <param name="prompt">The plain text user prompt.</param>
    /// <param name="cancellationToken">A token to cancel the inference request.</param>
    /// <returns>The complete assistant response text.</returns>
    public static Task<string> CompleteAsync(
        this IChatClient client,
        string prompt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(prompt);
        return client.CompleteAsync(new ChatMessage("user", prompt), cancellationToken);
    }

    /// <summary>
    /// Wraps a plain text user prompt in a <see cref="ChatMessage"/> and streams token deltas
    /// to <paramref name="onToken"/> as they are generated.
    /// </summary>
    /// <param name="client">The chat client instance.</param>
    /// <param name="prompt">The plain text user prompt.</param>
    /// <param name="onToken">Callback invoked for each non-empty token delta.</param>
    /// <param name="cancellationToken">A token to cancel the streaming request.</param>
    public static Task StreamAsync(
        this IChatClient client,
        string prompt,
        Func<string, Task> onToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(prompt);
        ArgumentNullException.ThrowIfNull(onToken);
        return client.StreamAsync(new ChatMessage("user", prompt), onToken, cancellationToken);
    }

    /// <summary>
    /// Bridges the callback-based <see cref="IChatClient.StreamAsync(ChatMessage, Func{string, Task}, CancellationToken)"/>
    /// into a pull-based asynchronous token stream. The pump task never throws; terminal failures
    /// are surfaced through the returned enumerable. Disposing the enumerator awaits completion
    /// of the in-flight turn. The client instance is not disposed by this method.
    /// </summary>
    /// <param name="client">The chat client instance.</param>
    /// <param name="message">The multimodal or text chat message to evaluate.</param>
    /// <param name="cancellationToken">A token to cancel the streaming request.</param>
    /// <returns>An asynchronous stream of non-empty token deltas.</returns>
    public static async IAsyncEnumerable<string> StreamAsync(
        this IChatClient client,
        ChatMessage message,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(message);
        var channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true
        });
        Task pump = PumpTokensAsync(client, message, channel.Writer, cancellationToken);
        try
        {
            await foreach (string token in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return token;
            }
        }
        finally
        {
            await pump.ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Wraps a plain text user prompt and exposes the response as a pull-based asynchronous token stream.
    /// </summary>
    /// <param name="client">The chat client instance.</param>
    /// <param name="prompt">The plain text user prompt.</param>
    /// <param name="cancellationToken">A token to cancel the streaming request.</param>
    /// <returns>An asynchronous stream of non-empty token deltas.</returns>
    public static IAsyncEnumerable<string> StreamAsync(
        this IChatClient client,
        string prompt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        return client.StreamAsync(new ChatMessage("user", prompt), cancellationToken);
    }

    private static async Task PumpTokensAsync(
        IChatClient client,
        ChatMessage message,
        ChannelWriter<string> writer,
        CancellationToken cancellationToken)
    {
        Exception? terminalException = null;
        try
        {
            await client.StreamAsync(
                message,
                async token => await writer.WriteAsync(token, cancellationToken).ConfigureAwait(false),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            terminalException = ex;
        }
        finally
        {
            writer.TryComplete(terminalException);
        }
    }
}