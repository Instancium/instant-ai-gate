# InstantAIGate — Client Integration Guide

**Version:** 1.5 (Multi-Model Runtime)  
**Target Audience:** Backend and frontend developers integrating client applications with the InstantAIGate Gateway via SignalR.

---

## Table of Contents
1. [Multi-Model Client Architecture](#multi-model-client-architecture)
2. [Authentication](#authentication)
3. [Connection Setup](#connection-setup)
4. [Gateway Status & Model Pool Discovery](#gateway-status--model-pool-discovery)
5. [Session Lifecycle](#session-lifecycle)
6. [Chat Streaming & Multimodal (Vision)](#chat-streaming--multimodal-vision)
7. [Context Management](#context-management)
8. [Embeddings & Auto-Routing](#embeddings--auto-routing)
9. [Model Management (Admin)](#model-management-admin)
10. [Model Download](#model-download)
11. [VRAM Management](#vram-management)
12. [Health Checks](#health-checks)
13. [Error Handling](#error-handling)
14. [Session Lifecycle & Network Resilience](#session-lifecycle--network-resilience)
15. [Complete Integration Example](#complete-integration-example)
16. [Configuration Reference](#configuration-reference)

---

## Multi-Model Client Architecture

InstantAIGate supports hosting multiple models concurrently in the VRAM pool (for example, a primary VLM/LLM chat model alongside a dedicated embedding model, or multiple LLMs of different sizes). Client applications should be designed around four core architectural principles:

### 1. Isolated Per-Model Context Pools
Every loaded model (`repoId`) maintains its own isolated context pool (`MaxContexts`) and concurrency semaphore in VRAM. High-throughput embedding requests or heavy sessions on one model will never starve or block context slots allocated to another model.

### 2. Primary Model vs. Active Model Pool
* **Primary Chat Model (`ActiveModelId`):** The gateway tracks a primary generative model (automatically set to the most recently loaded non-embedding model).
* **Loaded Model Pool (`ActiveModelIds`):** All models currently loaded and ready in VRAM are listed in `GatewayStatusDetails.ActiveModelIds`.
* **Runtime Introspection (`GetLoadedModelsDetailsAsync`):** Any `GatewayUser` client can query the full list of loaded models, their context sizes, and whether each model is an embedding model (`IsEmbedding: true`) or a generative chat/vision model (`IsEmbedding: false`).

### 3. Per-Session Model Binding
* Each stateful chat session (`sessionId`) is bound to a specific `repoId` when calling `JoinSession(sessionId, repoId)`.
* **Default Routing:** Passing an empty string (`""`) for `repoId` binds the session to the gateway's primary generative model (`ActiveModelId`).
* **Explicit Routing:** Passing a specific `repoId` (e.g., `"qwen3-vl-8b-instruct"`) binds the session to that model in the VRAM pool.
* **Multi-Session Multiplexing:** A single SignalR connection can hold multiple concurrent sessions bound to different models simultaneously.
* **Switching Models in UI:** Because native KV-caches cannot be shared across different model weights, switching models in a client UI requires closing the previous session (`LeaveSession(oldSessionId)`) and joining a new session (`JoinSession(newSessionId, newRepoId)`).

### 4. Zero-Config Embedding Auto-Routing
Embedding calls (`GenerateEmbeddingsAsync` and `GetEmbeddingDimensionAsync`) are **stateless** and do not require a session. Through `EmbeddingModelResolver`, clients do not need to hardcode or track the embedding model's `repoId`:
* If the client passes `""` (empty string) or even the `repoId` of a chat model, the gateway automatically routes the request to the loaded dedicated embedding model (`Embeddings: true` / `ModelType.Embedding`).
* If multiple embedding models are loaded in VRAM, the client can target a specific one by passing its explicit `repoId`.

---

## Authentication

All gateway requests (except health checks) require an API key passed as a Bearer token.

| Role | Access | Token Source |
| :--- | :--- | :--- |
| `GatewayUser` | Data plane (inference, sessions, status, loaded model discovery, embeddings) | Any non-empty valid token |
| `GatewayAdmin` | Control plane (model load/swap/unload, downloads, VRAM purge, queue limits) | Matches `InstantAIGate:AdminApiKey` config |

**Token Delivery Methods:**
* **HTTP Header:** `Authorization: Bearer <token>`
* **Query Parameter:** `?access_token=<token>` (standard for SignalR WebSocket negotiation)

```csharp
// C# SignalR client example
var connection = new HubConnectionBuilder()
    .WithUrl("http://your-server:5000/hub/gateway", options =>
    {
        options.AccessTokenProvider = () => Task.FromResult("your-api-key")!;
    })
    .WithAutomaticReconnect()
    .Build();
```

---

## Connection Setup

**Hub URL:** `/hub/gateway`

### Client Events to Subscribe (`GatewayUser`)
```csharp
connection.On<SessionTokenDelta>("ReceiveTokenDelta", delta => { /* handle token */ });
connection.On<string>("ReceiveError", error => { /* handle error */ });
connection.On<ContextOverflowException>("ReceiveContextOverflow", ex => { /* handle overflow */ });
connection.On<string>("ReceiveSessionClosed", sessionId => { /* session ended */ });
connection.On<DownloadProgress>("ReceiveDownloadProgress", progress => { /* download update */ });
connection.On<GatewayStatusDetails>("ReceiveGatewayStatus", status => { /* status change */ });
connection.On<int>("ReceiveQueuePosition", pos => { /* queue position */ });
```

### Admin-Only Events (`GatewayAdmin`)
```csharp
connection.On<InferenceMetrics, object>("ReceiveMetrics", (metrics, details) => { /* metrics & native details */ });
connection.On<string, string, string>("ReceiveLog", (level, category, message) => { /* server logs */ });
```

---

## Gateway Status & Model Pool Discovery

The gateway maintains a thread-safe operational state machine. Clients can query it via RPC or subscribe to real-time push updates.

### Query Gateway Status
```csharp
var status = await connection.InvokeAsync<GatewayStatusDetails>("GetGatewayStatus");
```
**Push:** The server automatically sends `ReceiveGatewayStatus` upon connection and whenever the state changes.

### `GatewayOperationalStatus` Enum
| Value | Numeric | Meaning |
| :--- | :--- | :--- |
| `Uninitialized` | `0` | No models are loaded in VRAM |
| `ModelDownloading` | `1` | Startup model is currently downloading |
| `ModelLoading` | `2` | A model is currently loading into VRAM |
| `Ready` | `3` | At least one model is loaded and ready for inference |
| `Faulted` | `4` | An error occurred during model initialization |

### `GatewayStatusDetails` Payload
```json
{
  "status": 3,
  "activeModelId": "qwen3-vl-2b-instruct",
  "activeModelIds": [
    "qwen3-vl-2b-instruct",
    "qwen3-embedding-0.6b"
  ],
  "stageDescription": "Gateway ready. Primary chat model: 'qwen3-vl-2b-instruct' (2 model(s) loaded).",
  "progressPercentage": 100.0,
  "downloadedBytes": 0,
  "totalBytes": 0,
  "bytesPerSecond": 0.0,
  "errorMessage": null,
  "timestamp": "2026-01-01T00:00:00Z"
}
```
* `activeModelId`: The default primary chat/VLM model used when `repoId` is omitted in `JoinSession`.
* `activeModelIds`: Read-only list of all model IDs currently loaded and ready in the VRAM pool.

### Discover Loaded Models in VRAM (`GatewayUser`)
To inspect all loaded models and their capabilities (e.g., separating chat models from embedding models):

```csharp
// Returns details for ALL models currently loaded in VRAM
var loadedModels = await connection.InvokeAsync<IEnumerable<NativeModelDetails>>("GetLoadedModelsDetailsAsync");

// Returns details for the primary active model
var primaryModel = await connection.InvokeAsync<NativeModelDetails>("GetActiveModelDetailsAsync");
```

### `NativeModelDetails` Fields
| Field | Type | Description |
| :--- | :--- | :--- |
| `RepoId` | `string` | Model repository identifier |
| `ContextSize` | `int` | Configured context window in tokens (`n_ctx`) |
| `GpuLayers` / `TotalLayers` | `int` | Offloaded GPU layer count vs. total model layers |
| `Threads` | `int` | CPU worker thread count |
| `FlashAttention` | `bool` | Whether FlashAttention is enabled |
| `IdleContextsCount` | `int` | Pooled idle contexts currently available in VRAM for this model |
| `Backend` | `string` | Native backend name (e.g., `llama.cpp`) |
| `EmbeddingDimension` | `int` | Embedding vector width (`n_embd`) reported by the native backend |
| `IsEmbedding` | `bool` | `true` if the model is configured for embedding generation |

---

## Session Lifecycle

### Join Session
```csharp
await connection.InvokeAsync("JoinSession", sessionId, repoId);
```
* If `repoId` is empty (`""`), the session binds to the primary chat model (`ActiveModelId`).
* If `repoId` is specified, the gateway verifies that the requested model is currently loaded in the VRAM pool (`ActiveModels`).
* Emits an error via `ReceiveError` if the gateway is not `Ready` or if the requested `repoId` is not active in memory.
* **Idempotent Reconnection:** Calling `JoinSession` with an existing `sessionId` rebinds the session to the caller's current `ConnectionId`, cancels any pending disconnect grace timer, and preserves the in-memory KV-cache.

### Leave Session (Soft Release)
```csharp
await connection.InvokeAsync("LeaveSession", sessionId);
```
Unbinds the session, clears the KV-cache, and returns the native context handle to that model's VRAM context pool for reuse. Sends `ReceiveSessionClosed` to the client.

### Destroy Session (Hard Release)
```csharp
await connection.InvokeAsync("DestroySession", sessionId);
```
Unbinds the session, sets `SuppressPool = true`, and forcibly frees the native context slot from VRAM. Sends `ReceiveSessionClosed` to the client.

> **Note:** Both `LeaveSession` and `DestroySession` await the session's `ExecutionGate` with `CancellationToken.None` on the server before freeing native memory, guaranteeing that any in-flight `llama_decode` call completes cleanly.

---

## Chat Streaming & Multimodal (Vision)

### Send Prompt Delta
```csharp
var message = new ChatMessage("user", new List<MessageContent>
{
    new TextContent("Hello, how are you?")
});
await connection.InvokeAsync("SendPromptDelta", sessionId, message);
```

### Receive Token Stream
```csharp
connection.On<SessionTokenDelta>("ReceiveTokenDelta", delta =>
{
    if (delta.SessionId == sessionId)
    {
        if (delta.IsDone)
        {
            // Stream complete. FinishReason: "stop"
        }
        else
        {
            Console.Write(delta.Content); // Append token piece
        }
    }
});
```

### Multimodal (Vision) Request
```csharp
var message = new ChatMessage("user", new List<MessageContent>
{
    new ImageFileContent("/path/to/image.jpg"),
    new TextContent("Describe this image")
});
await connection.InvokeAsync("SendPromptDelta", sessionId, message);
```

**Supported `MessageContent` Polymorphic Types (`type` discriminator):**
* `TextContent` (`"text"`) — plain text prompt.
* `ImageFileContent` (`"image_file"`) — local file path accessible by the server.
* `ImageBase64Content` (`"image_base64"`) — raw Base64 or `data:image/...;base64,...` URI.
* `ImageUrlContent` (`"image_url"`) — HTTP/HTTPS URL to an image (downloaded and cached by the server).

---

## Context Management

### Get Session Token Count
```csharp
int tokenCount = await connection.InvokeAsync<int>("GetSessionTokenCount", sessionId);
```

### Rollback (Truncate KV-Cache)
```csharp
await connection.InvokeAsync("RollbackSession", sessionId, targetPosition);
```
Removes all tokens at and after `targetPosition` (`[targetPosition, PastTokens)`). Use this to undo recent conversation turns or recover from context overflow when sliding window shifting is disabled.

### Shift (Sliding Window)
```csharp
await connection.InvokeAsync("ShiftSessionCache", sessionId, startPos, count);
```
Removes `count` tokens starting at `startPos` and shifts remaining tokens left. Frees KV-cache capacity while preserving recent history and system prefixes.

> **Important:** Throws `NotSupportedException` (delivered via `ReceiveError`) if `FlashAttention: true` or GPU offload settings prevent physical KV-cache shifting. Fallback: use `RollbackSession` or `LeaveSession`.

---

## Embeddings & Auto-Routing

The gateway exposes stateless vector generation and embedding dimension introspection over SignalR with built-in multi-model routing.

### Automatic Model Resolution (`EmbeddingModelResolver`)
When a client invokes `GenerateEmbeddingsAsync(repoId, request)` or `GetEmbeddingDimensionAsync(repoId)`:
1. If `repoId` matches a loaded model with `Embeddings: true`, that model is used directly.
2. If `repoId` is empty (`""`), `null`, or points to a non-embedding chat model (e.g., `"qwen3-vl-8b-instruct"`), the gateway **automatically routes** the request to the loaded dedicated embedding model (`Embeddings: true` or `ModelType.Embedding`).
3. If no embedding-capable model is currently loaded in VRAM, the hub throws a `HubException`.

### Generate Embeddings
```csharp
var request = new EmbeddingRequest("Text to embed", Normalize: true);

// Pass "" to automatically route to the active embedding model in VRAM,
// or pass an explicit repoId (e.g., "qwen3-embedding-0.6b").
var response = await connection.InvokeAsync<EmbeddingResponse>(
    "GenerateEmbeddingsAsync", "", request);

// response.Embedding          — float[] vector
// response.TokenCount         — tokens consumed by the input text
// response.EmbeddingDimension — vector size (equals response.Embedding.Length)
```

### `EmbeddingRequest` Fields
| Field | Type | Default | Description |
| :--- | :--- | :--- | :--- |
| `Text` | `string` | — | Input text to embed. Must be non-empty. |
| `Normalize` | `bool` | `true` | Apply $L_2$ normalization (unit-norm output vector). |
| `MaxTokens` | `int?` | `null` | Optional hard cap on the tokenized input length. |

### Query Embedding Dimension
Use dimension introspection to provision vector store indices before ingestion.

**Active RPC (`GatewayUser`):**
```csharp
// Pass "" for auto-resolution or an explicit repoId
int dimension = await connection.InvokeAsync<int>("GetEmbeddingDimensionAsync", "");
```

**Passive (via loaded models snapshot):**
```csharp
var models = await connection.InvokeAsync<IEnumerable<NativeModelDetails>>("GetLoadedModelsDetailsAsync");
int dimension = models.First(m => m.IsEmbedding).EmbeddingDimension;
```

### Error Cases
| Operation | Condition | Result |
| :--- | :--- | :--- |
| `GenerateEmbeddingsAsync` | No embedding-capable model is loaded in VRAM | `HubException` |
| `GenerateEmbeddingsAsync` | Input text exceeds model `ContextSize` | `HubException` (wraps `ContextOverflowException`) |
| `GetEmbeddingDimensionAsync` | No model / no matching model is active in VRAM | `HubException` |

### Recommended RAG Ingestion & Query Flow
```csharp
// 1. Verify gateway readiness
var status = await connection.InvokeAsync<GatewayStatusDetails>("GetGatewayStatus");
if (status.Status != GatewayOperationalStatus.Ready)
{
    throw new InvalidOperationException("Gateway is not ready.");
}

// 2. Provision the vector store using auto-resolved embedding model dimension
int dimension = await connection.InvokeAsync<int>("GetEmbeddingDimensionAsync", "");
vectorStore.EnsureIndex(dimension);

// 3. Embed and upsert document chunks (runs concurrently with chat sessions)
foreach (string chunk in documentChunks)
{
    var response = await connection.InvokeAsync<EmbeddingResponse>(
        "GenerateEmbeddingsAsync", "", new EmbeddingRequest(chunk, Normalize: true));
    vectorStore.Upsert(chunk, response.Embedding);
}
```

---

## Model Management (Admin)

All model lifecycle commands require the `GatewayAdmin` role.

### Load Model into VRAM Pool
```csharp
await connection.InvokeAsync("LoadModelAsync", repoId, profileName);
```
* Loads the specified model into the VRAM pool alongside any already loaded models.
* Profiles are defined in `appsettings.json` under `InstantAIGate:HardwareProfiles:{profileName}`.
* **Smart Profile Policy (`HardwareProfilePolicy`):** If `repoId` or the model's catalog architecture contains `"embed"` and the caller requests `"Default"` (or omits `profileName`), the server automatically routes the model to the `"Embedding"` hardware profile so `Embeddings: true` is enabled.

### Swap Model (Type-Scoped Hot-Swap)
```csharp
await connection.InvokeAsync("SwapModelAsync", repoId, profileName);
```
* Gracefully drains active leases and unloads **only** existing models of the **same `ModelType`** (`Llm`, `Vlm`, or `Embedding`), then loads the new model.
* Models of other types remain active and uninterrupted in VRAM (e.g., swapping a chat LLM does not unload your loaded `Embedding` model).

### Unload Model
```csharp
await connection.InvokeAsync("UnloadModelAsync", repoId);
```
* Marks `repoId` as draining, waits for any in-flight contexts on that specific model to finish, and frees its weights and context pool from VRAM.
* Other loaded models remain operational. The gateway only resets to `Uninitialized` if the last remaining model is unloaded.

### Get Model Registry Status
```csharp
var models = await connection.InvokeAsync<IEnumerable<ModelRegistryStatus>>("GetModelsAsync");
```

### Queue Metrics & Capacity
```csharp
var metrics = await connection.InvokeAsync<InferenceMetrics>("GetQueueMetricsAsync");
// metrics.ActiveLeases, metrics.PendingRequests

await connection.InvokeAsync("SetQueueLimitAsync", newLimit);
```

---

## Model Download

### Trigger & Monitor Download
```csharp
await connection.InvokeAsync("SubscribeToModelDownload", repoId);
await connection.InvokeAsync("DownloadModelAsync", repoId);

connection.On<DownloadProgress>("ReceiveDownloadProgress", progress =>
{
    Console.WriteLine($"{progress.ModelId}: {progress.Percentage:F1}% " +
                      $"({progress.SpeedBytesPerSecond / 1048576:F2} MB/s)");
});

// When finished:
await connection.InvokeAsync("UnsubscribeFromModelDownload", repoId);
```

---

## VRAM Management

### Purge Idle Contexts
```csharp
// Purge idle contexts for a specific model:
await connection.InvokeAsync("PurgeIdleContextsAsync", "qwen3-vl-2b-instruct");

// Or pass an empty string to purge idle contexts across ALL loaded models:
await connection.InvokeAsync("PurgeIdleContextsAsync", "");
```
Frees pooled idle contexts (`llama_free`) without unloading model weights. Use when VRAM pressure is high.

---

## Health Checks

| Endpoint | Auth | Purpose |
| :--- | :--- | :--- |
| `/health/live` | None | Liveness probe (always returns `200 OK`) |
| `/health/ready` | None | Readiness probe (`200 OK` when at least one model is loaded, `503` otherwise) |

**Ready Response (`200 OK`):**
```json
{
  "status": "Healthy",
  "description": "Model 'qwen3-vl-2b-instruct, qwen3-embedding-0.6b' is loaded and ready.",
  "data": {
    "status": "Ready",
    "activeModelId": "qwen3-vl-2b-instruct",
    "activeModels": [
      "qwen3-vl-2b-instruct",
      "qwen3-embedding-0.6b"
    ],
    "stageDescription": "Gateway ready. Primary chat model: 'qwen3-vl-2b-instruct' (2 model(s) loaded).",
    "progressPercentage": 100.0
  }
}
```

**Not Ready Response (`503 Service Unavailable`):**
```json
{
  "status": "Degraded",
  "description": "Downloading 'qwen3-vl-2b-instruct' (45.5%) (45.5%)",
  "data": {
    "status": "ModelDownloading",
    "activeModelId": "qwen3-vl-2b-instruct",
    "activeModels": [],
    "progressPercentage": 45.5,
    "bytesPerSecond": 25000000
  }
}
```

---

## Error Handling

### `ReceiveError`
General operational errors (requested model not active in pool, session not found, unsupported KV-cache shift):
```csharp
connection.On<string>("ReceiveError", error =>
{
    Console.WriteLine($"Gateway Error: {error}");
});
```

### `ReceiveContextOverflow`
Emitted when `PastTokens + IncomingTokens + ReservedTokens > ContextSize`:
```csharp
connection.On<ContextOverflowException>("ReceiveContextOverflow", ex =>
{
    // ex.SessionId, ex.PastTokens, ex.IncomingTokens, ex.ReservedTokens, ex.ContextSize
});
```

**Recommended Mitigation Flow:**
1. Try `ShiftSessionCache` (sliding window eviction).
2. If `NotSupportedException` occurs (e.g., `FlashAttention: true`), call `RollbackSession` to a safe token checkpoint.
3. To start fresh, call `DestroySession` (or `LeaveSession`) and open a new session with `JoinSession`.

### HTTP Error Codes (Middleware)
| Code | Exception | Meaning |
| :--- | :--- | :--- |
| `429` | `QueueFullException` | Gateway request queue is at maximum capacity |
| `504` | `TimeoutException` | Timed out waiting for an execution slot |
| `503` | `InvalidOperationException` (`draining` / `not active`) | Target model is currently unloading or inactive |
| `500` | `Exception` | Unhandled internal server error |

---

## Session Lifecycle & Network Resilience

InstantAIGate uses a connection-bound session architecture with a configurable disconnect Grace Period and idle timeout.

### Key Principles
1. **Connection Binding:** Calling `JoinSession(sessionId, repoId)` binds the session to the current SignalR `ConnectionId`.
2. **Disconnect Grace Period:** If the network drops, the server unbinds the connection and starts a grace timer (default: `30` seconds). The session and its native KV-cache remain intact in VRAM.
3. **Reconnection & KV-Cache Preservation:** Upon reconnecting, the client receives a new `ConnectionId` and must re-invoke `JoinSession(sessionId, repoId)` for each active session to cancel the eviction timer and preserve `PastTokens`.
4. **Grace Expiration & Idle Timeout:** Sessions that do not reconnect within the grace period, or remain idle longer than `IdleTimeoutMinutes` (default: `3` minutes), are automatically evicted and their contexts returned to the pool.

### Multi-Model Client Reconnection Pattern
Track a dictionary of `sessionId -> repoId` so every session rebinds to its exact model on reconnect:
```csharp
var activeSessions = new ConcurrentDictionary<string, string>();

hubConnection.Reconnected += async newConnectionId =>
{
    foreach (var (sessionId, repoId) in activeSessions)
    {
        await hubConnection.InvokeAsync("JoinSession", sessionId, repoId);
    }
};
```

---

## Complete Integration Example

```csharp
using System.Collections.Concurrent;
using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Dtos.Session;
using InstantAIGate.Core.Dtos.Status;
using Microsoft.AspNetCore.SignalR.Client;

var connection = new HubConnectionBuilder()
    .WithUrl("http://localhost:5000/hub/gateway", options =>
    {
        options.AccessTokenProvider = () => Task.FromResult("your-api-key")!;
    })
    .WithAutomaticReconnect()
    .Build();

// Map sessionId -> repoId for multi-model session recovery
var activeSessions = new ConcurrentDictionary<string, string>();

connection.Reconnected += async _ =>
{
    foreach (var (sid, modelId) in activeSessions)
    {
        await connection.InvokeAsync("JoinSession", sid, modelId);
    }
};

connection.On<SessionTokenDelta>("ReceiveTokenDelta", delta =>
{
    if (!delta.IsDone) Console.Write(delta.Content);
});
connection.On<string>("ReceiveError", err => Console.WriteLine($"ERROR: {err}"));
connection.On<GatewayStatusDetails>("ReceiveGatewayStatus", s =>
{
    Console.WriteLine($"Gateway: {s.Status} | Primary: {s.ActiveModelId} | Pool: [{string.Join(", ", s.ActiveModelIds)}]");
});
connection.On<string>("ReceiveSessionClosed", closedSessionId =>
{
    activeSessions.TryRemove(closedSessionId, out _);
});

await connection.StartAsync();

// 1. Check gateway readiness
var status = await connection.InvokeAsync<GatewayStatusDetails>("GetGatewayStatus");
if (status.Status != GatewayOperationalStatus.Ready)
{
    throw new InvalidOperationException("Gateway is not ready.");
}

// 2. Discover loaded models in VRAM
var loadedModels = (await connection.InvokeAsync<IEnumerable<NativeModelDetails>>("GetLoadedModelsDetailsAsync")).ToList();
string chatRepoId = loadedModels.FirstOrDefault(m => !m.IsEmbedding)?.RepoId ?? "";

// 3. Generate embeddings (auto-routed to the loaded embedding model even with "")
var embResponse = await connection.InvokeAsync<EmbeddingResponse>(
    "GenerateEmbeddingsAsync", "", new EmbeddingRequest("Search query for RAG context", Normalize: true));
Console.WriteLine($"Generated {embResponse.EmbeddingDimension}D embedding ({embResponse.TokenCount} tokens).");

// 4. Start a stateful chat session bound to the generative model
string sessionId = Guid.NewGuid().ToString("N");
await connection.InvokeAsync("JoinSession", sessionId, chatRepoId);
activeSessions[sessionId] = chatRepoId;

// 5. Stream chat completion
var message = new ChatMessage("user", "Hello!");
await connection.InvokeAsync("SendPromptDelta", sessionId, message);

// Wait for completion (use TaskCompletionSource or Channel in production)
await Task.Delay(5000);

// 6. Cleanup
await connection.InvokeAsync("LeaveSession", sessionId);
activeSessions.TryRemove(sessionId, out _);
await connection.StopAsync();
```

---

## Configuration Reference

### Multi-Model & Hardware Profiles (`appsettings.json`)
`InstantAIGate:StartupModel` supports loading multiple models at boot using the `Models` array (recommended for per-model profiles), a `RepoId` string array, or a single `RepoId` string:

```json
{
  "InstantAIGate": {
    "HardwareProfiles": {
      "Default": {
        "GpuLayerCount": 99,
        "MainGPU": 0,
        "ContextSize": 4096,
        "BatchSize": 512,
        "Threads": 8,
        "FlashAttention": true,
        "Embeddings": false,
        "KvCacheQuantization": "F16",
        "UseMemoryLock": false,
        "MaxContexts": 4
      },
      "Embedding": {
        "GpuLayerCount": 99,
        "MainGPU": 0,
        "ContextSize": 2048,
        "BatchSize": 512,
        "Threads": 4,
        "FlashAttention": true,
        "Embeddings": true,
        "KvCacheQuantization": "Q8_0",
        "UseMemoryLock": false,
        "MaxContexts": 2
      }
    },
    "StartupModel": {
      "Enabled": true,
      "Models": [
        {
          "RepoId": "qwen3-vl-2b-instruct",
          "Profile": "Default"
        },
        {
          "RepoId": "qwen3-embedding-0.6b",
          "Profile": "Embedding"
        }
      ]
    },
    "Sessions": {
      "DisconnectGracePeriodSeconds": 30,
      "IdleTimeoutMinutes": 3
    },
    "AdminApiKey": "your-secret-admin-key"
  }
}
```

### VRAM Budgeting Guidelines
| Model Size | `KvCacheQuantization` | `MaxContexts` | `FlashAttention` |
| :--- | :--- | :--- | :--- |
| $\le$ 8B | `F16` | `4` | `true` |
| 8B–14B | `Q8_0` | `3` | `true` |
| 14B–32B | `Q8_0` | `2` | `true` |
| $>$ 32B | `Q4_K` | `1` | `false` |

> **Critical:** Setting `FlashAttention: true` disables physical KV-cache shifting (`ShiftSessionCache`). Use `RollbackSession` as the fallback strategy.