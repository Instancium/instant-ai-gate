namespace InstantAIGate.Client.Chat;

using InstantAIGate.Client.Configuration;
using InstantAIGate.Client.Context;

/// <summary>
/// Factory contract for creating <see cref="IChatClient"/> instances with either a built-in
/// <see cref="ChatContextPolicy"/> or a custom <see cref="IChatContextStrategy"/>.
/// </summary>
public interface IChatClientFactory
{
    /// <summary>
    /// Creates an <see cref="IChatClient"/> configured with the specified built-in KV-cache policy.
    /// </summary>
    /// <param name="policy">The KV-cache lifecycle policy to apply.</param>
    /// <returns>A new <see cref="IChatClient"/> instance.</returns>
    IChatClient Create(ChatContextPolicy policy);

    /// <summary>
    /// Creates an <see cref="IChatClient"/> configured with a custom KV-cache context strategy.
    /// </summary>
    /// <param name="strategy">The custom KV-cache lifecycle strategy.</param>
    /// <returns>A new <see cref="IChatClient"/> instance.</returns>
    IChatClient Create(IChatContextStrategy strategy);
}
