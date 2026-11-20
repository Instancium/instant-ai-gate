namespace InstantAIGate.Client.Chat;

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using InstantAIGate.Client.Context;
using InstantAIGate.Core.Dtos.Inference;
using Microsoft.Extensions.Logging;

/// <summary>
/// Default <see cref="IChatMapReduceClient"/> implementation orchestrating a single checkpointed
/// session through warm-up, map, tree-reduce, and final phases. Buffered turns are retried with
/// session recreation and prefix re-warm-up on recoverable session loss; the streaming final turn
/// is retried only while no token has been delivered to the caller, preventing duplicate output.
/// </summary>
public sealed class ChatMapReduceClient : IChatMapReduceClient
{
    private readonly IChatClientFactory _factory;
    private readonly ILogger<ChatMapReduceClient> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChatMapReduceClient"/> class.
    /// </summary>
    /// <param name="factory">The chat client factory used to create checkpointed sessions.</param>
    /// <param name="logger">The logger for pipeline and recovery diagnostics.</param>
    public ChatMapReduceClient(IChatClientFactory factory, ILogger<ChatMapReduceClient> logger)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(logger);
        _factory = factory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string> CompleteAsync(ChatMapReduceRequest request, CancellationToken cancellationToken = default)
    {
        Validate(request);
        var builder = new StringBuilder();
        await foreach (string token in StreamAsync(request, cancellationToken).ConfigureAwait(false))
        {
            builder.Append(token);
        }
        return builder.ToString();
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<string> StreamAsync(
        ChatMapReduceRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Validate(request);
        request.Progress?.Report(new MapReduceProgress(
            MapReducePhase.Warmup, 0, 1, "Warming up shared KV-cache prefix..."));

        PipelineSession session = await CreateWarmedSessionAsync(request, cancellationToken).ConfigureAwait(false);
        try
        {
            string aggregated = await RunMapReduceAsync(session, request, cancellationToken).ConfigureAwait(false);
            ChatMessage finalPrompt = request.FinalPromptFactory(aggregated);
            request.Progress?.Report(new MapReduceProgress(
                MapReducePhase.Final, 0, 1, "Streaming final response..."));

            var channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = true
            });
            Task pump = PumpFinalTurnAsync(session, finalPrompt, request, channel.Writer, cancellationToken);
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
        finally
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task<string> RunMapReduceAsync(
        PipelineSession session,
        ChatMapReduceRequest request,
        CancellationToken cancellationToken)
    {
        int total = request.MapMessages.Count;
        var summaries = new List<string>(total);
        for (int i = 0; i < total; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            request.Progress?.Report(new MapReduceProgress(
                MapReducePhase.Map, i + 1, total, $"Analyzing part {i + 1}/{total} with KV-cache reuse..."));
            string summary = await ExecuteBufferedTurnAsync(session, request.MapMessages[i], request, cancellationToken)
                .ConfigureAwait(false);
            string trimmed = summary.Trim(' ', '\r', '\n', '`');
            if (!string.IsNullOrWhiteSpace(trimmed))
            {
                summaries.Add(trimmed);
            }
        }
        return await ReduceSummariesAsync(session, request, summaries, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> ReduceSummariesAsync(
        PipelineSession session,
        ChatMapReduceRequest request,
        List<string> summaries,
        CancellationToken cancellationToken)
    {
        string joined = string.Join("\n- ", summaries);
        if (joined.Length <= request.MaxAggregatedChars || summaries.Count <= request.ReduceGroupSize)
        {
            return joined;
        }

        int groupCount = (summaries.Count + request.ReduceGroupSize - 1) / request.ReduceGroupSize;
        var reduced = new List<string>(groupCount);
        for (int i = 0; i < summaries.Count; i += request.ReduceGroupSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int groupIndex = (i / request.ReduceGroupSize) + 1;
            request.Progress?.Report(new MapReduceProgress(
                MapReducePhase.Reduce, groupIndex, groupCount,
                $"Compressing summary group {groupIndex}/{groupCount}..."));

            int take = Math.Min(request.ReduceGroupSize, summaries.Count - i);
            string groupText = string.Join("\n- ", summaries.GetRange(i, take));
            ChatMessage prompt = request.ReducePromptFactory is { } factory
                ? factory(groupText)
                : new ChatMessage("user", BuildDefaultReducePrompt(groupText));
            string compressed = await ExecuteBufferedTurnAsync(session, prompt, request, cancellationToken)
                .ConfigureAwait(false);
            reduced.Add(compressed.Trim(' ', '\r', '\n', '`'));
        }
        return await ReduceSummariesAsync(session, request, reduced, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> ExecuteBufferedTurnAsync(
        PipelineSession session,
        ChatMessage message,
        ChatMapReduceRequest request,
        CancellationToken cancellationToken)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return await session.Client.CompleteAsync(message, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (attempt < request.MaxTurnAttempts && IsRecoverableSessionError(ex))
            {
                _logger.LogWarning(
                    ex,
                    "Session lost during a buffered turn; recreating and re-warming (attempt {Attempt}).",
                    attempt + 1);
                await RecreateSessionAsync(session, request, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task PumpFinalTurnAsync(
        PipelineSession session,
        ChatMessage finalPrompt,
        ChatMapReduceRequest request,
        ChannelWriter<string> writer,
        CancellationToken cancellationToken)
    {
        Exception? terminalException = null;
        try
        {
            for (int attempt = 1; ; attempt++)
            {
                bool yieldedAny = false;
                try
                {
                    await session.Client.StreamAsync(
                        finalPrompt,
                        async token =>
                        {
                            yieldedAny = true;
                            await writer.WriteAsync(token, cancellationToken).ConfigureAwait(false);
                        },
                        cancellationToken).ConfigureAwait(false);
                    return;
                }
                catch (Exception ex) when (!yieldedAny && attempt < request.MaxTurnAttempts && IsRecoverableSessionError(ex))
                {
                    _logger.LogWarning(
                        ex,
                        "Session lost before the first final token; recreating and re-warming (attempt {Attempt}).",
                        attempt + 1);
                    await RecreateSessionAsync(session, request, cancellationToken).ConfigureAwait(false);
                }
            }
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

    private async Task<PipelineSession> CreateWarmedSessionAsync(
        ChatMapReduceRequest request,
        CancellationToken cancellationToken)
    {
        IChatClient client = await CreateWarmedClientAsync(request, cancellationToken).ConfigureAwait(false);
        return new PipelineSession(client);
    }

    private async Task<IChatClient> CreateWarmedClientAsync(
        ChatMapReduceRequest request,
        CancellationToken cancellationToken)
    {
        IChatClient client = _factory.Create(new CheckpointedChatContextStrategy());
        try
        {
            await client.CompleteAsync(request.WarmupMessage, cancellationToken).ConfigureAwait(false);
            return client;
        }
        catch
        {
            await client.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task RecreateSessionAsync(
        PipelineSession session,
        ChatMapReduceRequest request,
        CancellationToken cancellationToken)
    {
        IChatClient replacement = await CreateWarmedClientAsync(request, cancellationToken).ConfigureAwait(false);
        await session.ReplaceAsync(replacement).ConfigureAwait(false);
    }

    private static bool IsRecoverableSessionError(Exception exception)
    {
        return exception is GatewaySessionLostException or IOException
            || (exception is InvalidOperationException
                && (exception.Message.Contains("not active", StringComparison.OrdinalIgnoreCase)
                    || exception.Message.Contains("was not found", StringComparison.OrdinalIgnoreCase)));
    }

    private static string BuildDefaultReducePrompt(string groupText)
    {
        return $"""
            Combine and compress the following partial review notes into a dense, cohesive summary.
            Keep all architectural details but remove redundant phrasing.
            NOTES:
            - {groupText}
            """;
    }

    private static void Validate(ChatMapReduceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.WarmupMessage);
        ArgumentNullException.ThrowIfNull(request.MapMessages);
        ArgumentNullException.ThrowIfNull(request.FinalPromptFactory);
        if (request.MapMessages.Count == 0)
        {
            throw new ArgumentException("At least one map message is required.", nameof(request));
        }
        foreach (ChatMessage message in request.MapMessages)
        {
            ArgumentNullException.ThrowIfNull(message, nameof(request));
        }
        ArgumentOutOfRangeException.ThrowIfLessThan(request.MaxAggregatedChars, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(request.ReduceGroupSize, 2);
        ArgumentOutOfRangeException.ThrowIfLessThan(request.MaxTurnAttempts, 1);
    }

    private sealed class PipelineSession : IAsyncDisposable
    {
        public PipelineSession(IChatClient client)
        {
            Client = client;
        }

        public IChatClient Client { get; private set; }

        public async Task ReplaceAsync(IChatClient replacement)
        {
            IChatClient previous = Client;
            Client = replacement;
            await previous.DisposeAsync().ConfigureAwait(false);
        }

        public ValueTask DisposeAsync() => Client.DisposeAsync();
    }
}
