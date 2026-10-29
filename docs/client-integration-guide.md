InstantAIGate — Client Integration Guide
Version: 1.5
Target Audience: Backend/frontend developers integrating with InstantAIGate Gateway via SignalR.

Table of Contents
[Authentication](#authentication)
[Connection Setup](#connection-setup)
[Gateway Status Monitoring](#gateway-status-monitoring)
[Session Lifecycle](#session-lifecycle)
[Chat Streaming](#chat-streaming)
[Context Management](#context-management)
[Embeddings](#embeddings)
[Model Management (Admin)](#model-management-admin)
[Model Download](#model-download)
[VRAM Management](#vram-management)
[Health Checks](#health-checks)
[Error Handling](#error-handling)
[Session Lifecycle & Network Resilience](#session-lifecycle--network-resilience)
[Complete Integration Example](#complete-integration-example)
[Configuration Reference](#configuration-reference)

Authentication
All gateway requests require an API key passed as a Bearer token.
| Role|Access|Token Source|
| ---|---|---|
| GatewayUser|Data plane (inference, sessions, status, embeddings)|Any valid token|
| GatewayAdmin|Control plane (model management, downloads, VRAM)|Matches  InstantAIGate:AdminApiKey  config|
Token Delivery Methods:
HTTP Header: `Authorization: Bearer <token>`
Query Parameter: `?access_token=<token>` (for SignalR WebSocket)
// C# SignalR client example
var connection = new HubConnectionBuilder()
    .WithUrl("http://your-server:5000/hub/gateway", options =>
    {
        options.AccessTokenProvider = () => Task.FromResult("your-api-key")!;
    })
    .WithAutomaticReconnect()
    .Build();

Connection Setup
Hub URL:`/hub/gateway`
Client Events to Subscribe:
connection.On<SessionTokenDelta>("ReceiveTokenDelta", delta => { /* handle token */ });
connection.On<string>("ReceiveError", error => { /* handle error */ });
connection.On<ContextOverflowException>("ReceiveContextOverflow", ex => { /* handle overflow */ });
connection.On<string>("ReceiveSessionClosed", sessionId => { /* session ended */ });
connection.On<DownloadProgress>("ReceiveDownloadProgress", progress => { /* download update */ });
connection.On<GatewayStatusDetails>("ReceiveGatewayStatus", status => { /* status change */ });
connection.On<int>("ReceiveQueuePosition", pos => { /* queue position */ });
Admin-only Events:
connection.On<InferenceMetrics, object>("ReceiveMetrics", (metrics, details) => { /* metrics */ });
connection.On<string, string, string>("ReceiveLog", (level, category, message) => { /* logs */ });

Gateway Status Monitoring
The gateway maintains an operational state machine. Query it via RPC or receive push updates.
RPC:
var status = await connection.InvokeAsync<GatewayStatusDetails>("GetGatewayStatus");
Push: Server automatically sends `ReceiveGatewayStatus` on connection and on state change.
Status Enum:
| Value | Meaning |
|-------|---------|
| `Uninitialized` | No model loaded |
| `ModelDownloading` | Startup model downloading |
| `ModelLoading` | Model loading into VRAM |
| `Ready` | Model loaded, inference available |
| `Faulted` | Error occurred during initialization |
GatewayStatusDetails Fields:
{
  "status": 3,
  "activeModelId": "qwen3-vl-2b-instruct",
  "stageDescription": "Model is ready for inference",
  "progressPercentage": 100.0,
  "downloadedBytes": 0,
  "totalBytes": 0,
  "bytesPerSecond": 0.0,
  "errorMessage": null,
  "timestamp": "2026-01-01T00:00:00Z"
}

Session Lifecycle
Join Session
await connection.InvokeAsync("JoinSession", sessionId, repoId);
If `repoId` is empty, uses currently active model.
Throws error via `ReceiveError` if gateway is not `Ready`.
Throws error if requested model is not the active model.
Calling `JoinSession` with an existing `sessionId` is idempotent: it rebinds the session to the current connection, cancels any pending disconnect grace eviction, and preserves existing KV-cache state.

Leave Session (Soft Release)
await connection.InvokeAsync("LeaveSession", sessionId);
Unbinds the session, clears session state, and returns the context to the VRAM context pool for reuse. Sends `ReceiveSessionClosed` to the client.

Destroy Session (Hard Release)
await connection.InvokeAsync("DestroySession", sessionId);
Unbinds the session, suppresses VRAM pooling (`SuppressPool = true`), and forcibly destroys the context slot. Sends `ReceiveSessionClosed` to the client.
Note: Both `LeaveSession` and `DestroySession` await `ExecutionGate` with `CancellationToken.None` on the server before freeing native memory, guaranteeing that any in-flight native generation completes cleanly. Client callers should ensure timeouts accommodate active token generation.

Chat Streaming
Send Prompt
var message = new ChatMessage("user", new List<MessageContent>
{
    new TextContent("Hello, how are you?")
});
await connection.InvokeAsync("SendPromptDelta", sessionId, message);
Receive Tokens
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
            Console.Write(delta.Content); // Append token
        }
    }
});
Multimodal (Vision) Request
var message = new ChatMessage("user", new List<MessageContent>
{
    new ImageFileContent("/path/to/image.jpg"),
    new TextContent("Describe this image")
});
await connection.InvokeAsync("SendPromptDelta", sessionId, message);
Supported content types:
`TextContent` — plain text
`ImageFileContent` — local file path (server-side)
`ImageBase64Content` — base64-encoded image
`ImageUrlContent` — HTTP URL to image

Context Management
Get Token Count
int tokenCount = await connection.InvokeAsync<int>("GetSessionTokenCount", sessionId);
Rollback (Truncate KV-cache)
await connection.InvokeAsync("RollbackSession", sessionId, targetPosition);
Removes all tokens after `targetPosition`. Use when user wants to "undo" last messages.
Shift (Sliding Window)
await connection.InvokeAsync("ShiftSessionCache", sessionId, startPos, count);
Removes `count` tokens starting at `startPos` and shifts remaining tokens. Frees VRAM while preserving recent context.
Note: Throws `NotSupportedException` if FlashAttention or GPU-offload is active (physical shift impossible). Fallback: use `RollbackSession` + re-decode.

Embeddings
The gateway exposes the full embedding workflow over SignalR: vector generation, vector-dimension introspection, and model metadata reporting.

Generate Embeddings
var request = new EmbeddingRequest("Text to embed", Normalize: true);
var response = await connection.InvokeAsync<EmbeddingResponse>(
    "GenerateEmbeddingsAsync", repoId, request);
// response.Embedding          — float[] vector
// response.TokenCount         — tokens consumed by the input text
// response.EmbeddingDimension — vector size (equals response.Embedding.Length)
EmbeddingRequest Fields:
| Field|Type|Default|Description|
| ---|---|---|---|
| `Text`|`string`|—|Input text to embed. Must be non-empty.|
| `Normalize`|`bool`|`true`|Apply L2 normalization (unit-norm output vector).|
| `MaxTokens`|`int?`|`null`|Optional hard cap on the tokenized input length.|

Query Embedding Dimension
Use dimension introspection to provision vector stores (create index columns) before ingestion, or to validate model compatibility without generating a vector.
Active RPC (data plane, GatewayUser):
int dimension = await connection.InvokeAsync<int>("GetEmbeddingDimensionAsync", repoId);
Passive (via model details snapshot):
var details = await connection.InvokeAsync<NativeModelDetails>("GetActiveModelDetailsAsync");
int dimension = details.EmbeddingDimension;
Notes:
The dimension is a static property of the loaded model (`n_embd`) and is available as soon as the gateway is `Ready`.
Introspection does NOT require `Embeddings: true` in the hardware profile; only `GenerateEmbeddingsAsync` requires it.
The call reads the loaded model handle directly: no inference context is allocated and no VRAM pool is affected.

Prerequisites:
Model must be loaded with `Embeddings: true` in hardware profile (generation only).
Model must be the active model.
Input text must fit within context window.

Error Cases:
| Operation|Condition|Result|
| ---|---|---|
| `GenerateEmbeddingsAsync`|Gateway not `Ready`|`HubException`|
| `GenerateEmbeddingsAsync`|Requested model not active|`HubException`|
| `GenerateEmbeddingsAsync`|Text exceeds context size|`HubException` (wraps `ContextOverflowException`)|
| `GenerateEmbeddingsAsync`|Profile lacks `Embeddings: true`|`HubException` (wraps `InvalidOperationException`)|
| `GetEmbeddingDimensionAsync`|Gateway not `Ready`|`HubException`|
| `GetEmbeddingDimensionAsync`|Requested repoId is not the active model|`HubException`|

Recommended RAG Ingestion Flow
// 1. Verify gateway readiness
var status = await connection.InvokeAsync<GatewayStatusDetails>("GetGatewayStatus");
if (status.Status != GatewayOperationalStatus.Ready)
{
    throw new InvalidOperationException("Gateway is not ready for embedding workloads.");
}
// 2. Provision the vector store with the exact dimension
int dimension = await connection.InvokeAsync<int>("GetEmbeddingDimensionAsync", repoId);
vectorStore.EnsureIndex(dimension);
// 3. Embed and upsert document chunks
foreach (string chunk in documentChunks)
{
    var response = await connection.InvokeAsync<EmbeddingResponse>(
        "GenerateEmbeddingsAsync", repoId, new EmbeddingRequest(chunk, Normalize: true));
    vectorStore.Upsert(chunk, response.Embedding);
}

Model Management (Admin)
Load Model
await connection.InvokeAsync("LoadModelAsync", repoId, profileName);
Profiles are defined in `appsettings.json` under `InstantAIGate:HardwareProfiles:{profileName}`.
Swap Model (Hot-Swap)
await connection.InvokeAsync("SwapModelAsync", repoId, profileName);
Gracefully drains active requests, unloads old model, loads new model. Queue is paused during swap.
Unload Model
await connection.InvokeAsync("UnloadModelAsync", repoId);
Get Active Model Details
var details = await connection.InvokeAsync<NativeModelDetails>("GetActiveModelDetailsAsync");
// details.RepoId, details.ContextSize, details.GpuLayers, details.Backend,
// details.EmbeddingDimension, etc.
NativeModelDetails Fields:
| Field|Description|
| ---|---|
| `RepoId`|Active model identifier|
| `ContextSize`|Configured context window (tokens)|
| `GpuLayers` / `TotalLayers`|Offloaded layer count vs total layer count|
| `Threads`|Worker thread count|
| `FlashAttention`|Whether FlashAttention is enabled|
| `IdleContextsCount`|Pooled idle contexts currently held in VRAM|
| `Backend`|Native backend name (e.g., `llama.cpp`)|
| `EmbeddingDimension`|Embedding vector width (n_embd) reported by the native backend|
Get Queue Metrics
var metrics = await connection.InvokeAsync<InferenceMetrics>("GetQueueMetricsAsync");
// metrics.ActiveLeases, metrics.PendingRequests
Set Queue Limit
await connection.InvokeAsync("SetQueueLimitAsync", newLimit);

Model Download
Trigger Download
await connection.InvokeAsync("SubscribeToModelDownload", repoId);
await connection.InvokeAsync("DownloadModelAsync", repoId);
Monitor Progress
connection.On<DownloadProgress>("ReceiveDownloadProgress", progress =>
{
    Console.WriteLine($"{progress.ModelId}: {progress.Percentage:F1}% " +
                      $"({progress.SpeedBytesPerSecond / 1048576:F2} MB/s)");
});
Unsubscribe
await connection.InvokeAsync("UnsubscribeFromModelDownload", repoId);

VRAM Management
Purge Idle Contexts
await connection.InvokeAsync("PurgeIdleContextsAsync", repoId);
Frees all pooled idle contexts for the specified model. Use when VRAM pressure is high and no active sessions need the cached contexts.

Health Checks
| Endpoint|Auth|Purpose|
| ---|---|---|
| /health/live|None|Liveness probe (always 200)|
| /health/ready|None|Readiness probe (200 when model loaded, 503 otherwise)|
Ready Response (200):
{ "status": "Healthy", "description": "Model 'qwen3-vl-2b-instruct' is loaded and ready." }
Not Ready Response (503):
{
  "status": "Degraded",
  "description": "Downloading model 'qwen3-vl-2b-instruct' (45.5%)",
  "data": {
    "status": "ModelDownloading",
    "progressPercentage": 45.5,
    "bytesPerSecond": 25000000
  }
}

Error Handling
ReceiveError
General errors (model not found, session not active, etc.):
connection.On<string>("ReceiveError", error =>
{
    Console.WriteLine($"Gateway Error: {error}");
});
ReceiveContextOverflow
Context window exceeded. Client should offer mitigation:
connection.On<ContextOverflowException>("ReceiveContextOverflow", ex =>
{
    // ex.PastTokens, ex.IncomingTokens, ex.ReservedTokens, ex.ContextSize
    // Options: ShiftSessionCache, RollbackSession, or clear session
});
Recommended Mitigation Flow:
Try `ShiftSessionCache` (sliding window).
If `NotSupportedException` → `RollbackSession` to safe position.
If user wants fresh start → `DestroySession` + new `JoinSession`.
HTTP Error Codes (Middleware)
| Code|Exception|Meaning|
| ---|---|---|
| 429|QueueFullException|Gateway at capacity|
| 504|TimeoutException|Queue wait timeout|
| 503|InvalidOperationException  (draining)|Model swapping|
| 500|Other|Internal error|

Session Lifecycle & Network Resilience
InstantAIGate uses a connection-bound session architecture with a configurable disconnect Grace Period and idle timeout.

Key Principles:
1. Connection Binding: When a client calls `JoinSession(sessionId, repoId)`, the session is bound to the current SignalR `ConnectionId`.
2. Disconnect Grace Period: When a client connection drops (e.g., Wi-Fi hiccup, network reconnect), the server unbinds the connection and arms a delayed eviction timer (default: 30 seconds). The session and its KV-cache stay intact in VRAM.
3. Reconnection & KV-Cache Preservation: When the client reconnects, its `HubConnection` receives a new `ConnectionId`. The client must invoke `JoinSession(sessionId, repoId)` with the original `sessionId`. This cancels the eviction timer, rebinds the session to the new connection, and preserves all previously decoded tokens (`PastTokens`).
4. Grace Expiration: If the client does not reconnect within the Grace Period, the session is evicted safely, and its context is returned to the pool.
5. Idle Timeout: Connected sessions with no activity for longer than the idle timeout (default: 3 minutes) are automatically evicted.
6. ExecutionGate Safety: Native context disposal always waits for active generation (`llama_decode`) to complete, avoiding native crashes.
7. Event Visibility: Automatic grace and idle evictions are silent because the client transport is disconnected or inactive. The `ReceiveSessionClosed` event is emitted specifically on explicit `LeaveSession` or `DestroySession` invocations.

Client Reconnection Handling Pattern:
```csharp
hubConnection.Reconnected += async connectionId =>
{
    // Re-join active sessions to rebind connection and cancel grace eviction
    foreach (var sessionId in activeSessions)
    {
        await hubConnection.InvokeAsync("JoinSession", sessionId, activeRepoId);
    }
};
```

Complete Integration Example
using Microsoft.AspNetCore.SignalR.Client;
var connection = new HubConnectionBuilder()
    .WithUrl("http://localhost:5000/hub/gateway", options =>
    {
        options.AccessTokenProvider = () => Task.FromResult("your-api-key");
    })
    .WithAutomaticReconnect()
    .Build();

var activeSessions = new HashSet<string>();
string currentRepoId = "qwen3-vl-2b-instruct";

// Reconnection hook: rebind sessions during grace period to keep KV-cache hot
connection.Reconnected += async newConnectionId =>
{
    foreach (var id in activeSessions)
    {
        await connection.InvokeAsync("JoinSession", id, currentRepoId);
    }
};

// Subscribe to events
connection.On<SessionTokenDelta>("ReceiveTokenDelta", delta =>
{
    if (!delta.IsDone) Console.Write(delta.Content);
});
connection.On<string>("ReceiveError", err => Console.WriteLine($"ERROR: {err}"));
connection.On<GatewayStatusDetails>("ReceiveGatewayStatus", s =>
{
    Console.WriteLine($"Gateway: {s.Status} | Model: {s.ActiveModelId}");
});
connection.On<string>("ReceiveSessionClosed", closedSessionId =>
{
    activeSessions.Remove(closedSessionId);
});

await connection.StartAsync();

// Check gateway readiness
var status = await connection.InvokeAsync<GatewayStatusDetails>("GetGatewayStatus");
if (status.Status != GatewayOperationalStatus.Ready)
{
    Console.WriteLine("Waiting for model...");
    // Wait for ReceiveGatewayStatus with Ready
}

// Create session
string sessionId = Guid.NewGuid().ToString("N");
await connection.InvokeAsync("JoinSession", sessionId, currentRepoId);
activeSessions.Add(sessionId);

// Send prompt
var message = new ChatMessage("user", "Hello!");
await connection.InvokeAsync("SendPromptDelta", sessionId, message);

// Wait for completion (use TaskCompletionSource or Channels in production)
await Task.Delay(5000);

// Cleanup
await connection.InvokeAsync("LeaveSession", sessionId);
activeSessions.Remove(sessionId);
await connection.StopAsync();

Configuration Reference
Hardware Profiles (appsettings.json)
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
         "ContextSize": 2048,
         "BatchSize": 512,
         "Threads": 4,
         "FlashAttention": true,
         "Embeddings": true,
         "KvCacheQuantization": "Q8_0",
         "MaxContexts": 2
       }
     },
         "StartupModel": {
           "Enabled": true,
           "RepoId": "qwen3-vl-2b-instruct",
           "Profile": "Default"
         },
         "Sessions": {
           "DisconnectGracePeriodSeconds": 30,
           "IdleTimeoutMinutes": 3
         },
         "AdminApiKey": "your-secret-admin-key"
       }
     }
VRAM Budgeting Guidelines
| Model Size|KvCacheQuantization|MaxContexts|FlashAttention|
| ---|---|---|---|
| ≤ 8B|F16|4|true|
| 8B–14B|Q8_0|3|true|
| 14B–32B|Q8_0|2|true|
| > 32B|Q4_K|1|false|
Critical:`FlashAttention: true` disables `ShiftSessionCache`. Use `RollbackSession` as fallback.