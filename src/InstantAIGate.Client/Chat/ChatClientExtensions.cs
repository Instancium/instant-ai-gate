namespace InstantAIGate.Client.Chat;

using System;
using System.Threading;
using System.Threading.Tasks;
using InstantAIGate.Core.Dtos.Inference;

/// <summary>
/// Convenience extension methods for <see cref="IChatClient"/> allowing plain string prompts
/// without requiring callers to construct <see cref="ChatMessage"/> wrappers manually.
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
}
