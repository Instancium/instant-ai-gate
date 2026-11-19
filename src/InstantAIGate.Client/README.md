# InstantAIGate.Client

Official .NET SignalR client SDK for **InstantAIGate**. Provides a typed, capability-segregated transport layer and reusable KV-cache context strategies over `/hub/gateway`.

## Architectural Boundaries

`InstantAIGate.Client` is designed as a **thin, composable SDK**.

### In Scope
- **Transport Plane (`IGatewayConnection`):** SignalR `HubConnection` lifecycle, Bearer token authentication, reconnect policies, and gateway readiness gating (`GatewayOperationalStatus.Ready`).
- **Capability Clients (ISP):**
  - `IChatClient` — multimodal `ChatMessage` streaming and completion.
  - `IGatewayEmbeddingClient` — dense vector generation returning `InstantAIGate.Core.Dtos.Inference.EmbeddingResponse`.
  - `IGatewayTelemetryClient` — read-only Data Plane diagnostics (gateway status, loaded model details, session token count).
- **Context Strategy Plane (`IChatContextStrategy`):** Mechanical KV-cache lifecycle primitives mapped directly to `IGatewayHub` (`Stateless`, `Conversational`, `Checkpointed`).

### Strictly Out of Scope
- **Domain / Product Orchestration:** RAG pipelines, citation parsing, Git diff optimization, chat history storage, or UI-level dialogue surgery (Compact / Trim / Nuke). These belong in consumer applications (`Instancium.Lens`, `Instancium.Chat`, `Instancium.Publisher`).
- **Third-Party Engine Adapters:** No references to vector databases (e.g., `Instancium.Vectra`) or UI frameworks. Consumers implement thin adapters in their own projects.
- **Control Plane (Admin):** Administrative hub operations (`IGatewayHubAdmin`) are excluded from the standard Data Plane client.

## Evolution & Contribution Rules

1. **Extend via Strategies and Extensions, Not Parameters:** New KV-cache behaviors must implement `IChatContextStrategy` rather than adding boolean flags to `GatewayChatClient`.
2. **Zero Consumer Leakage:** Never hardcode consumer names (e.g., `lens-*`) in session IDs or error messages. Use `GatewayOptions.ClientId`.
3. **No Magic Strings:** All SignalR hub method names must reference `nameof(IGatewayHub.*)` via `GatewayHubMethods`.
4. **Synchronized Releases:** `InstantAIGate.Client` is versioned and packed alongside `InstantAIGate.Core` to guarantee wire-protocol compatibility.
5. **XML Documentation:** All public types and members require XML docs (`///`) explaining **why** the contract exists.

## Quick Start

```csharp
services.AddInstantAIGateClient(options =>
{
    options.BaseUrl = "http://localhost:5000";
    options.ApiKey = Environment.GetEnvironmentVariable("Gateway__ApiKey")!;
    options.ClientId = "my-app";
});
```

```csharp
await using var chat = chatClientFactory.Create(ChatContextPolicy.Stateless);
await chat.StreamAsync("Explain KV-cache rollback.", async token =>
{
    Console.Write(token);
    await Task.CompletedTask;
});
```

## License

Licensed under the Apache License 2.0. See `LICENSE` for details.
