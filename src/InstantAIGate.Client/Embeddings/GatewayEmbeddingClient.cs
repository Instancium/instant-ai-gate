namespace InstantAIGate.Client.Embeddings;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using InstantAIGate.Client.Transport;
using InstantAIGate.Core.Dtos.Inference;
using Microsoft.AspNetCore.SignalR.Client;

/// <summary>
/// SignalR-backed <see cref="IGatewayEmbeddingClient"/> with lazy dimension validation
/// against <see cref="Configuration.GatewayOptions.ExpectedEmbeddingDimension"/>.
/// </summary>
public sealed class GatewayEmbeddingClient : IGatewayEmbeddingClient
{
    private readonly IGatewayConnection _connection;
    private readonly SemaphoreSlim _validationLock = new(1, 1);
    private bool _dimensionValidated;

    /// <summary>
    /// Initializes a new instance of the <see cref="GatewayEmbeddingClient"/> class.
    /// </summary>
    /// <param name="connection">The shared gateway SignalR connection.</param>
    public GatewayEmbeddingClient(IGatewayConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        _connection = connection;
    }

    /// <inheritdoc />
    public async Task<EmbeddingResponse> EmbedAsync(
        string text,
        bool normalize = true,
        int? maxTokens = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);

        await _connection.EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        await EnsureExpectedDimensionAsync(cancellationToken).ConfigureAwait(false);

        var hub = await _connection.GetConnectedHubAsync(cancellationToken).ConfigureAwait(false);
        var request = new EmbeddingRequest(text, normalize, maxTokens);

        var response = await hub.InvokeAsync<EmbeddingResponse>(
            GatewayHubMethods.GenerateEmbeddingsAsync,
            _connection.Options.EmbeddingRepoId,
            request,
            cancellationToken).ConfigureAwait(false);

        ValidateResponseDimension(response);
        return response;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<EmbeddingResponse>> EmbedBatchAsync(
        IReadOnlyList<string> texts,
        bool normalize = true,
        int? maxTokens = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(texts);

        if (texts.Count == 0)
        {
            return Array.Empty<EmbeddingResponse>();
        }

        await _connection.EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        await EnsureExpectedDimensionAsync(cancellationToken).ConfigureAwait(false);

        var hub = await _connection.GetConnectedHubAsync(cancellationToken).ConfigureAwait(false);
        var results = new List<EmbeddingResponse>(texts.Count);

        foreach (var text in texts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(text, nameof(texts));

            var request = new EmbeddingRequest(text, normalize, maxTokens);
            var response = await hub.InvokeAsync<EmbeddingResponse>(
                GatewayHubMethods.GenerateEmbeddingsAsync,
                _connection.Options.EmbeddingRepoId,
                request,
                cancellationToken).ConfigureAwait(false);

            ValidateResponseDimension(response);
            results.Add(response);
        }

        return results;
    }

    /// <inheritdoc />
    public async Task<int> GetDimensionAsync(CancellationToken cancellationToken = default)
    {
        await _connection.EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        var hub = await _connection.GetConnectedHubAsync(cancellationToken).ConfigureAwait(false);

        return await hub.InvokeAsync<int>(
            GatewayHubMethods.GetEmbeddingDimensionAsync,
            _connection.Options.EmbeddingRepoId,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureExpectedDimensionAsync(CancellationToken cancellationToken)
    {
        if (_dimensionValidated || _connection.Options.ExpectedEmbeddingDimension is not int expected)
        {
            return;
        }

        await _validationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_dimensionValidated)
            {
                return;
            }

            var hub = await _connection.GetConnectedHubAsync(cancellationToken).ConfigureAwait(false);
            var actual = await hub.InvokeAsync<int>(
                GatewayHubMethods.GetEmbeddingDimensionAsync,
                _connection.Options.EmbeddingRepoId,
                cancellationToken).ConfigureAwait(false);

            if (actual != expected)
            {
                throw new InvalidOperationException(
                    $"Embedding dimension mismatch: expected {expected}, but gateway model reported {actual}.");
            }

            _dimensionValidated = true;
        }
        finally
        {
            _validationLock.Release();
        }
    }

    private void ValidateResponseDimension(EmbeddingResponse response)
    {
        if (_connection.Options.ExpectedEmbeddingDimension is int expected &&
            response.EmbeddingDimension != expected)
        {
            throw new InvalidOperationException(
                $"Embedding response dimension mismatch: expected {expected}, but received {response.EmbeddingDimension}.");
        }
    }
}
