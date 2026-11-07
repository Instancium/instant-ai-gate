# InstantAIGate — Client Integration Guide

**Version:** 1.5.1 (Multi-Model Runtime)  
**Target Audience:** Backend and frontend developers integrating client applications with the InstantAIGate Gateway via SignalR.

This document contains all the necessary information, architectural rules, data contracts, and methods required to implement a robust client application for the InstantAIGate inference runtime.

---

## Table of Contents
1. [Multi-Model Client Architecture](#1-multi-model-client-architecture)
2. [Authentication & Connection](#2-authentication--connection)
3. [Data Plane API (Inference & Sessions)](#3-data-plane-api-inference--sessions)
4. [Control Plane API (Administration)](#4-control-plane-api-administration)
5. [Data Contracts (DTOs)](#5-data-contracts-dtos)
6. [Gateway Status & Telemetry](#6-gateway-status--telemetry)
7. [Session Lifecycle & Network Resilience](#7-session-lifecycle--network-resilience)
8. [Error Handling & Mitigations](#8-error-handling--mitigations)
9. [Complete C# Client Example](#9-complete-c-client-example)
10. [Configuration Reference](#10-configuration-reference)

---

## 1. Multi-Model Client Architecture

InstantAIGate supports hosting multiple models concurrently in the VRAM pool (e.g., a primary VLM/LLM chat model alongside a dedicated embedding model). Clients should be designed around four core architectural principles:

### 1.1 Isolated Per-Model Context Pools
Every loaded model (`repoId`) maintains its own isolated context pool (`MaxContexts`) in VRAM. High-throughput embedding requests on one model will never block context slots allocated to a chat model.

### 1.2 Primary Model vs. Active Model Pool
* **Primary Chat Model (`ActiveModelId`):** The gateway tracks a primary generative model (the most recently loaded non-embedding model).
* **Loaded Model Pool (`ActiveModelIds`):** All models currently loaded and ready in VRAM are listed in `GatewayStatusDetails.ActiveModelIds`.
* **Runtime Introspection:** Clients can query the full list of loaded models via `GetLoadedModelsDetailsAsync` to discover their context sizes and capabilities (`IsEmbedding`).

### 1.3 Per-Session Model Binding
* Each stateful chat session (`sessionId`) must be bound to a specific `repoId` via `JoinSession(sessionId, repoId)`.
* **Default Routing:** Passing an empty string (`""`) binds the session to the gateway's primary generative model (`ActiveModelId`).
* **Multi-Session Multiplexing:** A single SignalR connection can hold multiple concurrent sessions bound to different models.
* **Switching Models:** Native KV-caches cannot be shared across different model weights. Switching models in a UI requires calling `LeaveSession(oldSessionId)` and `JoinSession(newSessionId, newRepoId)`.

### 1.4 Zero-Config Embedding Auto-Routing
Embedding calls (`GenerateEmbeddingsAsync`) are **stateless**. Through the `EmbeddingModelResolver`, clients do not need to hardcode the embedding model's `repoId`:
* Passing `""` automatically routes the request to the loaded dedicated embedding model (`ModelType.Embedding`).
* If multiple embedding models are loaded, clients can target a specific one by passing its explicit `repoId`.

---

## 2. Authentication & Connection

All gateway requests (except HTTP health checks) require an API key passed as a Bearer token. **Both Data Plane and Control Plane share the same SignalR endpoint**, but methods are guarded by Role-Based Access Control (RBAC).

**Hub URL:** `ws://<your-server>/hub/gateway` (or `http(s)://...`)

| Role | Access | Token Source |
| :--- | :--- | :--- |
| `GatewayUser` | Data plane (inference, sessions, status, embeddings) | Any non-empty valid token |
| `GatewayAdmin` | Control plane (load/unload, telemetry, VRAM purge) | Matches `InstantAIGate:AdminApiKey` config |

**Token Delivery Methods:**
* **HTTP Header:** `Authorization: Bearer <token>`
* **Query Parameter:** `?access_token=<token>` (Standard for browser-based SignalR WebSocket negotiation)

---

## 3. Data Plane API (Inference & Sessions)
*Requires `GatewayUser` role.*

Clients invoke these methods to manage conversational state, stream tokens, and generate embeddings.

### 3.1 Client-to-Server Methods (RPC)

| Method Signature | Description |
| :--- | :--- |
| `JoinSession(string sessionId, string repoId)` | Creates or resumes a stateful chat session. Binds the session to the current SignalR connection. |
| `LeaveSession(string sessionId)` | Soft-releases the session, returning the native context to the VRAM pool. |
| `DestroySession(string sessionId)` | Hard-destroys the session, permanently freeing the native context slot from VRAM. |
| `SendPromptDelta(string sessionId, ChatMessage deltaMessage)` | Submits a multimodal prompt and initiates token streaming. |
| `RollbackSession(string sessionId, int targetPosition)` | Truncates the session KV-cache to a specific token boundary. |
| `ShiftSessionCache(string sessionId, int startPos, int count)` | Applies sliding-window eviction to the KV-cache. |
| `Task<int> GetSessionTokenCount(string sessionId)` | Returns the number of `PastTokens` cached in the session. |
| `Task<GatewayStatusDetails> GetGatewayStatus()` | Returns the current operational state of the gateway. |
| `Task<NativeModelDetails> GetActiveModelDetailsAsync()` | Returns details for the primary active model. |
| `Task<IEnumerable<NativeModelDetails>> GetLoadedModelsDetailsAsync()`| Returns details for all models in VRAM. |
| `SubscribeToModelDownload(string repoId)` | Joins the broadcast group to receive `ReceiveDownloadProgress`. |
| `UnsubscribeFromModelDownload(string repoId)` | Leaves the download broadcast group. |
| `Task<EmbeddingResponse> GenerateEmbeddingsAsync(string repoId, EmbeddingRequest request)` | Generates semantic vectors. Auto-routes if `repoId` is `""`. |
| `Task<int> GetEmbeddingDimensionAsync(string repoId)` | Queries the embedding dimension (`n_embd`) of the model. |

### 3.2 Server-to-Client Events (Push)
Your client must register handlers (`connection.on(...)`) for these events.

| Event Signature | Description |
| :--- | :--- |
| `ReceiveTokenDelta(SessionTokenDelta delta)` | A generated text chunk or completion marker (`IsDone = true`). |
| `ReceiveContextOverflow(ContextOverflowReport report)` | Emitted when prompt + generated tokens exceed `ContextSize`. |
| `ReceiveError(string error)` | Operational error (e.g., target model inactive, unsupported shift). |
| `ReceiveSessionClosed(string sessionId)` | Confirmation that `LeaveSession` or `DestroySession` succeeded. |
| `ReceiveDownloadProgress(DownloadProgress progress)` | Real-time download metrics (requires subscription). |
| `ReceiveGatewayStatus(GatewayStatusDetails status)` | Pushed automatically when the server state changes. |

---

## 4. Control Plane API (Administration)
*Requires `GatewayAdmin` role.*

These methods are used by Dashboards or orchestrator agents to manage the server's lifecycle.

### 4.1 Client-to-Server Methods (RPC)

| Method Signature | Description |
| :--- | :--- |
| `LoadModelAsync(string repoId, string? profileName)` | Loads a model into VRAM. Auto-routes embeddings to the `Embedding` profile. |
| `SwapModelAsync(string repoId, string? profileName)` | Hot-swaps models of the **same type** (unloads existing, loads new). |
| `UnloadModelAsync(string repoId)` | Gracefully drains and unloads a specific model from VRAM. |
| `DownloadModelAsync(string repoId)` | Enqueues a background download job for a model. |
| `PurgeIdleContextsAsync(string repoId)` | Frees pooled idle contexts for `repoId` (or all models if `""`). |
| `SetQueueLimitAsync(int limit)` | Adjusts the dynamic inference request queue concurrency limit. |
| `TriggerGarbageCollectionAsync()` | Forces a .NET garbage collection (LOH/SOH). |
| `ForceKillSessionAsync(string sessionId)` | Instantly destroys a hanging session and frees its VRAM slot. |
| `Task<IEnumerable<ModelRegistryStatus>> GetModelsAsync()` | Retrieves the internal status of all tracked models. |
| `Task<InferenceMetrics> GetQueueMetricsAsync()` | Retrieves current active leases and pending queue requests. |
| `Task<DashboardTelemetrySnapshot> GetDashboardSnapshotAsync()` | Retrieves a full point-in-time telemetry snapshot. |
| `SubscribeToDashboardTelemetry()` | Subscribes to 1Hz telemetry broadcasts (`ReceiveDashboardTelemetry`). |
| `UnsubscribeFromDashboardTelemetry()` | Leaves the dashboard telemetry group. |
| `Task<IReadOnlyList<SessionTelemetryDto>> GetActiveSessionsAsync()`| Lists detailed metrics for all active inference sessions. |

### 4.2 Server-to-Client Events (Push)

| Event Signature | Description |
| :--- | :--- |
| `ReceiveLog(string level, string category, string msg)` | Live application and native (`llama.cpp`) logs. |
| `ReceiveMetrics(InferenceMetrics metrics, object details)` | Live updates on queue depth and active context leases. |
| `ReceiveDashboardTelemetry(DashboardTelemetrySnapshot snap)`| Comprehensive 1Hz update (requires subscription). |

---

## 5. Data Contracts (DTOs)

Below are the JSON shapes of the critical data structures used in the RPC calls.

### ChatMessage & MessageContent
Passed to `SendPromptDelta`. Supports polymorphic multimodal content.
```json
{
  "role": "user",
  "parts": [
    { "type": "image_url", "url": "https://example.com/image.jpg" },
    { "type": "text", "text": "What is in this image?" }
  ]
}
```
*Content Types:* `text`, `image_base64`, `image_file`, `image_url`.

### SessionTokenDelta
Received via `ReceiveTokenDelta`.
```json
{
  "sessionId": "guid-1234",
  "content": "Hello",
  "isDone": false,
  "finishReason": null
}
```
*Note:* When the stream completes, the server sends an empty `content` with `isDone: true` and `finishReason: "stop"` (or `"length"`).

### EmbeddingRequest & Response
```json
// Request
{
  "text": "String to embed",
  "normalize": true,
  "maxTokens": null
}

// Response
{
  "embedding": [0.012, -0.045, ...],
  "tokenCount": 4,
  "embeddingDimension": 1024
}
```

### ContextOverflowReport
Received via `ReceiveContextOverflow`.
```json
{
  "sessionId": "guid-1234",
  "pastTokens": 4000,
  "incomingTokens": 150,
  "reservedTokens": 256,
  "contextSize": 4096
}
```

---

## 6. Gateway Status & Telemetry

### GatewayOperationalStatus Enum
`0 = Uninitialized`, `1 = ModelDownloading`, `2 = ModelLoading`, `3 = Ready`, `4 = Faulted`

### GatewayStatusDetails
```json
{
  "status": 3,
  "activeModelId": "qwen3-vl-2b-instruct",
  "activeModelIds": [
    "qwen3-vl-2b-instruct",
    "qwen3-embedding-0.6b"
  ],
  "stageDescription": "Gateway ready.",
  "progressPercentage": 100.0,
  "errorMessage": null
}
```

---

## 7. Session Lifecycle & Network Resilience

InstantAIGate uses a connection-bound session architecture with a **Grace Period**.

1. **Connection Binding:** `JoinSession(sessionId, repoId)` binds the session to the current SignalR `ConnectionId`.
2. **Disconnect Grace Period:** If the network drops, the server starts a grace timer (default: `30` seconds). The session and its native KV-cache remain intact in VRAM.
3. **Reconnection:** Upon reconnecting, the client receives a new `ConnectionId` and MUST re-invoke `JoinSession` for each active session to cancel the eviction timer and preserve `PastTokens`.
4. **Idle Timeout:** Sessions that do not reconnect, or remain idle longer than `IdleTimeoutMinutes` (default: `3` minutes), are automatically evicted.

**Reconnection Implementation (TypeScript/JavaScript Example):**
```typescript
const activeSessions = new Map<string, string>(); // sessionId -> repoId

connection.onreconnected(async (connectionId) => {
    for (const [sessionId, repoId] of activeSessions.entries()) {
        await connection.invoke("JoinSession", sessionId, repoId);
    }
});
```

---

## 8. Error Handling & Mitigations

### 8.1 Context Overflow
If prompt + generated tokens exceed `ContextSize`, the server aborts decoding and sends `ReceiveContextOverflow`.
**Mitigation Strategy:**
1. Try `ShiftSessionCache(sessionId, startPos, count)` to evict old messages while keeping the system prompt.
2. If `ShiftSessionCache` fails (e.g., `ReceiveError` due to `FlashAttention: true` rejecting memory shifts), fallback to `RollbackSession(sessionId, targetPosition)`.
3. As a last resort, `DestroySession` and start fresh.

### 8.2 HTTP Errors (REST Middleware)
* **`429 Too Many Requests`**: Gateway request queue limit reached (`QueueFullException`).
* **`504 Gateway Timeout`**: Timed out waiting for an execution slot.
* **`503 Service Unavailable`**: Target model is currently unloading or inactive.
* **`401 Unauthorized`**: Missing or invalid `Authorization: Bearer <token>`.

---

## 9. Complete C# Client Example

```csharp
using System.Collections.Concurrent;
using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Dtos.Status;
using Microsoft.AspNetCore.SignalR.Client;

var connection = new HubConnectionBuilder()
    .WithUrl("http://localhost:5000/hub/gateway", options =>
    {
        options.AccessTokenProvider = () => Task.FromResult("your-tenant-key")!;
    })
    .WithAutomaticReconnect()
    .Build();

var activeSessions = new ConcurrentDictionary<string, string>();

// Handle Reconnection
connection.Reconnected += async _ =>
{
    foreach (var (sid, modelId) in activeSessions)
    {
        await connection.InvokeAsync("JoinSession", sid, modelId);
    }
};

// Wire up events
connection.On<SessionTokenDelta>("ReceiveTokenDelta", delta =>
{
    if (!delta.IsDone) Console.Write(delta.Content);
    else Console.WriteLine($"\n[Stream Complete: {delta.FinishReason}]");
});
connection.On<string>("ReceiveError", err => Console.WriteLine($"\nERROR: {err}"));

await connection.StartAsync();

// 1. Generate embeddings (Auto-routed to the loaded embedding model)
var embResponse = await connection.InvokeAsync<EmbeddingResponse>(
    "GenerateEmbeddingsAsync", "", new EmbeddingRequest("Search query", Normalize: true));
Console.WriteLine($"Generated {embResponse.EmbeddingDimension}D embedding.");

// 2. Start a stateful chat session (Auto-routed to primary chat model)
string sessionId = Guid.NewGuid().ToString("N");
await connection.InvokeAsync("JoinSession", sessionId, "");
activeSessions[sessionId] = ""; // tracking for reconnects

// 3. Stream chat completion
var message = new ChatMessage("user", new[] { new TextContent("Hello!") });
await connection.InvokeAsync("SendPromptDelta", sessionId, message);

// ... Wait for completion ...
await Task.Delay(5000);

// 4. Cleanup
await connection.InvokeAsync("LeaveSession", sessionId);
activeSessions.TryRemove(sessionId, out _);
await connection.StopAsync();
```

---

## 10. Configuration Reference

Relevant `appsettings.json` server options influencing client interactions:

```json
{
  "InstantAIGate": {
    "HardwareProfiles": {
      "Default": {
        "ContextSize": 4096,
        "MaxContexts": 4,
        "FlashAttention": true
      },
      "Embedding": {
        "ContextSize": 2048,
        "MaxContexts": 2,
        "Embeddings": true
      }
    },
    "Sessions": {
      "DisconnectGracePeriodSeconds": 30,
      "IdleTimeoutMinutes": 3
    }
  }
}
```
* **FlashAttention:** When `true`, clients cannot use `ShiftSessionCache` (sliding window) because physical KV-cache memory blocks cannot be shifted. Clients must use `RollbackSession` instead.