namespace InstantAIGate.Core.Tests.Server;

using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;
using InstantAIGate.Core.Interfaces.Hub;
// using InstantAIGate.Server.Hubs; // Requires project reference to Server

/// <summary>
/// Architectural guard to prevent reverse desynchronization between GatewayHub implementation and Core contracts.
/// </summary>
public class GatewayHubArchitectureTests
{
    [Fact]
    public void GatewayHub_MustNotExpose_UndocumentedPublicEndpoints()
    {
        // Arrange
        // Note: Use Type.GetType or typeof() depending on your test project references.
        // Assuming the test project references InstantAIGate.Server:
        var hubImplementationType = typeof(InstantAIGate.Server.Hubs.GatewayHub);

        var allowedInterfaces = new[]
        {
            typeof(IGatewayHub),
            typeof(IGatewayHubAdmin)
        };

        var expectedMethods = allowedInterfaces
            .SelectMany(i => i.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            .Select(m => m.Name)
            .ToHashSet(StringComparer.Ordinal);


        // Get all public methods declared directly in GatewayHub (ignore base Hub methods like OnConnectedAsync)
        var actualPublicMethods = hubImplementationType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName) // Ignore property getters/setters
            .Where(m => m.Name != "OnConnectedAsync" && m.Name != "OnDisconnectedAsync") // Ignore SignalR lifecycle overrides
            .Where(m => m.ReturnType == typeof(Task) || m.ReturnType.IsGenericType && m.ReturnType.GetGenericTypeDefinition() == typeof(Task<>))
            .ToList();

        // Assert
        var orphanedMethods = actualPublicMethods
            .Where(m => !expectedMethods.Contains(m.Name))
            .Select(m => m.Name)
            .ToList();

        Assert.True(orphanedMethods.Count == 0,
            $"Architectural Violation: GatewayHub exposes public methods not defined in IGatewayHub or IGatewayHubAdmin. " +
            $"Orphaned methods: {string.Join(", ", orphanedMethods)}. " +
            $"Action: Add these to the Core interfaces or make them private.");
    }
}