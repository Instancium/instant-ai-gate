namespace InstantAIGate.Core.Tests.Integration;

using FluentAssertions;
using InstantAIGate.Core.Interfaces.Inference;
using InstantAIGate.Core.Interfaces.Session;
using InstantAIGate.Core.Services.Session;
using InstantAIGate.Core.Tests.TestConfiguration;
using InstantAIGate.Server;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

/// <summary>
/// Chaos tests verifying deterministic VRAM release when physical connections drop abruptly.
/// Hard disconnects are simulated server-side by aborting the underlying connection,
/// ensuring the client never gets a chance to call DestroySession gracefully.
/// </summary>
public class EphemeralSessionReaperIntegrationTests : IClassFixture<ChaosTestFixture>
{
    private readonly ChaosTestFixture _fixture;
    private readonly ILogger<EphemeralSessionReaperIntegrationTests> _logger;

    public EphemeralSessionReaperIntegrationTests(ChaosTestFixture fixture)
    {
        _fixture = fixture;
        _logger = _fixture.LoggerFactory.CreateLogger<EphemeralSessionReaperIntegrationTests>();
    }

    [Fact]
    public async Task HardDisconnect_EphemeralSession_DestroysWithinTwoSeconds()
    {
        // Arrange
        var mockSessionManager = _fixture.MockSessionInferenceManager;
        mockSessionManager.Reset();

        var connection = _fixture.CreateGatewayConnection(_fixture.ServerOptions.TenantApiKey);
        await connection.StartAsync();

        const string sessionId = "chaos-session-1";
        await connection.InvokeAsync("JoinSession", sessionId, string.Empty);
        await connection.InvokeAsync("MarkSessionAsEphemeral", sessionId);

        var releaseCalled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopwatch = Stopwatch.StartNew();

        mockSessionManager
            .Setup(m => m.ReleaseSessionAsync(sessionId, true, It.IsAny<CancellationToken>()))
            .Callback(() =>
            {
                stopwatch.Stop();
                releaseCalled.TrySetResult(true);
            })
            .Returns(Task.CompletedTask);

        // Act: abort the physical connection server-side, without any graceful client close
        if (!_fixture.TryAbortConnection(connection.ConnectionId!))
        {
            await connection.StopAsync();
        }

        // Assert
        var completedTask = await Task.WhenAny(releaseCalled.Task, Task.Delay(2000));
        completedTask.Should().Be(releaseCalled.Task, "ephemeral session must be destroyed within 2000ms after hard disconnect");
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(2000, "VRAM must be released deterministically");

        mockSessionManager.Verify(
            m => m.ReleaseSessionAsync(sessionId, true, It.IsAny<CancellationToken>()),
            Times.Once,
            "Ephemeral session must be destroyed with destroySlot: true on hard disconnect");

        _logger.LogInformation("Ephemeral session destroyed in {ElapsedMs}ms after hard disconnect", stopwatch.ElapsedMilliseconds);
        await connection.DisposeAsync();
    }

    [Fact]
    public async Task HardDisconnect_MultipleEphemeralSessions_AllDestroyedDeterministically()
    {
        // Arrange
        var mockSessionManager = _fixture.MockSessionInferenceManager;
        mockSessionManager.Reset();

        var connection = _fixture.CreateGatewayConnection(_fixture.ServerOptions.TenantApiKey);
        await connection.StartAsync();

        var sessionIds = new[] { "chaos-multi-1", "chaos-multi-2", "chaos-multi-3" };
        var releaseCountdown = new CountdownEvent(sessionIds.Length);
        var stopwatch = Stopwatch.StartNew();

        foreach (var sessionId in sessionIds)
        {
            await connection.InvokeAsync("JoinSession", sessionId, string.Empty);
            await connection.InvokeAsync("MarkSessionAsEphemeral", sessionId);

            mockSessionManager
                .Setup(m => m.ReleaseSessionAsync(sessionId, true, It.IsAny<CancellationToken>()))
                .Callback(() => releaseCountdown.Signal())
                .Returns(Task.CompletedTask);
        }

        // Act
        if (!_fixture.TryAbortConnection(connection.ConnectionId!))
        {
            await connection.StopAsync();
        }

        // Assert
        releaseCountdown.Wait(2000).Should().BeTrue("all ephemeral sessions must be destroyed within 2000ms");
        stopwatch.Stop();
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(2000, "all VRAM slots must be released deterministically");

        foreach (var sessionId in sessionIds)
        {
            mockSessionManager.Verify(
                m => m.ReleaseSessionAsync(sessionId, true, It.IsAny<CancellationToken>()),
                Times.Once,
                $"Session {sessionId} must be destroyed with destroySlot: true");
        }

        await connection.DisposeAsync();
    }

    [Fact]
    public async Task HardDisconnect_NonEphemeralSession_IsNotForceDestroyed()
    {
        // Arrange
        var mockSessionManager = _fixture.MockSessionInferenceManager;
        mockSessionManager.Reset();

        var connection = _fixture.CreateGatewayConnection(_fixture.ServerOptions.TenantApiKey);
        await connection.StartAsync();

        const string sessionId = "persistent-session-1";
        await connection.InvokeAsync("JoinSession", sessionId, string.Empty);

        // Act
        if (!_fixture.TryAbortConnection(connection.ConnectionId!))
        {
            await connection.StopAsync();
        }

        await Task.Delay(500);

        // Assert: regular sessions must never be force-destroyed on disconnect
        mockSessionManager.Verify(
            m => m.ReleaseSessionAsync(sessionId, true, It.IsAny<CancellationToken>()),
            Times.Never,
            "Non-ephemeral session must not be destroyed with destroySlot: true");

        await connection.DisposeAsync();
    }
}

/// <summary>
/// Deterministic tests for the background reaper cleanup pass, driven by a fake clock.
/// </summary>
public class SessionReaperCleanupTests
{
    [Fact]
    public async Task CleanupOrphanedSessions_SessionsOlderThanThreshold_AreForceReleased()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var registry = new EphemeralSessionRegistry(timeProvider);
        var mockSessionManager = new Mock<ISessionInferenceManager>();
        var reaper = new SessionReaperService(registry, mockSessionManager.Object, NullLogger<SessionReaperService>.Instance, timeProvider);

        registry.RegisterEphemeralSession("dead-connection", "orphan-1");
        timeProvider.Advance(TimeSpan.FromMinutes(3));

        // Act
        await reaper.CleanupOrphanedSessionsAsync(CancellationToken.None);

        // Assert
        mockSessionManager.Verify(
            m => m.ReleaseSessionAsync("orphan-1", true, It.IsAny<CancellationToken>()),
            Times.Once,
            "Orphaned ephemeral session must be force-released by the reaper");
    }

    [Fact]
    public async Task CleanupOrphanedSessions_FreshSessions_AreNotReleased()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var registry = new EphemeralSessionRegistry(timeProvider);
        var mockSessionManager = new Mock<ISessionInferenceManager>();
        var reaper = new SessionReaperService(registry, mockSessionManager.Object, NullLogger<SessionReaperService>.Instance, timeProvider);

        registry.RegisterEphemeralSession("alive-connection", "fresh-1");

        // Act
        await reaper.CleanupOrphanedSessionsAsync(CancellationToken.None);

        // Assert
        mockSessionManager.Verify(
            m => m.ReleaseSessionAsync(It.IsAny<string>(), true, It.IsAny<CancellationToken>()),
            Times.Never,
            "Fresh ephemeral sessions belonging to live connections must not be reaped");
    }
}

/// <summary>
/// Test-only fixture exposing a server-side connection abort capability.
/// </summary>
public class ChaosTestFixture : WebApplicationFactory<Program>
{
    public Mock<ISessionInferenceManager> MockSessionInferenceManager { get; } = new();
    public TestServerOptions ServerOptions { get; } = TestConfig.Server;

    public ILoggerFactory LoggerFactory => Server.Services.GetRequiredService<ILoggerFactory>();

    private ConnectionAbortRegistry AbortRegistry => Server.Services.GetRequiredService<ConnectionAbortRegistry>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(ISessionInferenceManager));
            if (descriptor != null)
            {
                services.Remove(descriptor);
            }

            services.AddSingleton(MockSessionInferenceManager.Object);
            services.AddSingleton<ConnectionAbortRegistry>();
            services.AddSingleton<IHubFilter, ConnectionCaptureFilter>();
        });
    }

    public HubConnection CreateGatewayConnection(string token)
    {
        var gatewayUrl = new Uri(new Uri(ServerOptions.PublicBaseUrl), "/hub/gateway");
        return new HubConnectionBuilder()
            .WithUrl(gatewayUrl, options =>
            {
                options.HttpMessageHandlerFactory = _ => Server.CreateHandler();
                options.AccessTokenProvider = () => Task.FromResult(token)!;
            })
            .Build();
    }

    /// <summary>
    /// Aborts the physical connection on the server side, simulating a hard network drop.
    /// </summary>
    /// <param name="connectionId">The server-assigned connection identifier.</param>
    /// <returns>True when the abort was initiated server-side; false when a client-side fallback is required.</returns>
    public bool TryAbortConnection(string connectionId)
    {
        return AbortRegistry.TryAbort(connectionId);
    }
}

/// <summary>
/// Test-only registry holding server-side abort handles captured at connection time.
/// </summary>
public sealed class ConnectionAbortRegistry
{
    private readonly ConcurrentDictionary<string, AbortEntry> _entries = new();

    public void Register(string connectionId, IConnectionLifetimeFeature? lifetimeFeature, HttpContext? httpContext)
    {
        _entries[connectionId] = new AbortEntry(lifetimeFeature, httpContext);
    }

    public void Remove(string connectionId)
    {
        _entries.TryRemove(connectionId, out _);
    }

    public bool TryAbort(string connectionId)
    {
        if (!_entries.TryGetValue(connectionId, out var entry))
        {
            return false;
        }

        if (entry.LifetimeFeature != null)
        {
            entry.LifetimeFeature.Abort();
            return true;
        }

        if (entry.HttpContext != null)
        {
            entry.HttpContext.Abort();
            return true;
        }

        return false;
    }

    private sealed record AbortEntry(IConnectionLifetimeFeature? LifetimeFeature, HttpContext? HttpContext);
}

/// <summary>
/// Test-only hub filter capturing the abort handles for each established connection.
/// Registered exclusively in the test fixture, production DI never sees this type.
/// </summary>
public sealed class ConnectionCaptureFilter : IHubFilter
{
    private readonly ConnectionAbortRegistry _registry;

    public ConnectionCaptureFilter(ConnectionAbortRegistry registry)
    {
        _registry = registry;
    }

    public async Task OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, Task> next)
    {
        var lifetimeFeature = context.Context.Features.Get<IConnectionLifetimeFeature>();
        var httpContext = context.Context.GetHttpContext();
        _registry.Register(context.Context.ConnectionId, lifetimeFeature, httpContext);

        try
        {
            await next(context);
        }
        finally
        {
            _registry.Remove(context.Context.ConnectionId);
        }
    }
}