# InstantAIGate — Client Integration Guide

**Version:** 1.3
**Target Audience:** Backend/frontend developers integrating with InstantAIGate Gateway via SignalR.

---

## Table of Contents

1. [Authentication](#authentication)
2. [Connection Setup](#connection-setup)
3. [Gateway Status Monitoring](#gateway-status-monitoring)
4. [Session Lifecycle](#session-lifecycle)
5. [Chat Streaming](#chat-streaming)
6. [Context Management](#context-management)
7. [Embeddings](#embeddings)
8. [Model Management (Admin)](#model-management-admin)
9. [Model Download](#model-download)
10. [VRAM Management](#vram-management)
11. [Health Checks](#health-checks)
12. [Error Handling](#error-handling)
13. [Ephemeral Sessions](#ephemeral-sessions)

---

## Authentication

All gateway requests require an API key passed as a Bearer token.

| Role | Access | Token Source |
|------|--------|--------------|
| `GatewayUser` | Data plane (inference, sessions, status) | Any valid token |
| `GatewayAdmin` | Control plane (model management, downloads, VRAM) | Matches `InstantAIGate:AdminApiKey` config |

**Token Delivery Methods:**
1. HTTP Header: `Authorization: Bearer <token>`
2. Query Parameter: `?access_token=<token>` (for SignalR WebSocket)

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

**Client Events to Subscribe:**

```csharp
connection.On<SessionTokenDelta>("ReceiveTokenDelta", delta => { /* handle token */ });
connection.On<string>("ReceiveError", error => { /* handle error */ });
connection.On<ContextOverflowException>("ReceiveContextOverflow", ex => { /* handle overflow */ });
connection.On<string>("ReceiveSessionClosed", sessionId => { /* session ended */ });
connection.On<DownloadProgress>("ReceiveDownloadProgress", progress => { /* download update */ });
connection.On<GatewayStatusDetails>("ReceiveGatewayStatus", status => { /* status change */ });
connection.On<int>("ReceiveQueuePosition", pos => { /* queue position */ });
```

**Admin-only Events:**
```csharp
connection.On<InferenceMetrics, object>("ReceiveMetrics", (metrics, details) => { /* metrics */ });
connection.On<string, string, string>("ReceiveLog", (level, category, message) => { /* logs */ });
```

---

## Gateway Status Monitoring

The gateway maintains an operational state machine. Query it via RPC or receive push updates.

**RPC:**
```csharp
var status = await connection.InvokeAsync<GatewayStatusDetails>("GetGatewayStatus");
```

**Push:** Server automatically sends `ReceiveGatewayStatus` on connection and on state change.

**Status Enum:**
| Value | Meaning |
|-------|---------|
| `Uninitialized` | No model loaded |
| `ModelDownloading` | Startup model downloading |
| `ModelLoading` | Model loading into VRAM |
| `Ready` | Model loaded, inference available |
| `Faulted` | Error occurred during initialization |

**GatewayStatusDetails Fields:**
```json
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
```

---

## Session Lifecycle

### Join Session

```csharp
await connection.InvokeAsync("JoinSession", sessionId, repoId);
```

- If `repoId` is empty, uses currently active model.
- Throws error via `ReceiveError` if gateway is not `Ready`.
- Throws error if requested model is not the active model.

### Leave Session (Soft Release)

```csharp
await connection.InvokeAsync("LeaveSession", sessionId);
```

Releases session state but preserves VRAM context for potential reuse.

### Destroy Session (Hard Release)

```csharp
await connection.InvokeAsync("DestroySession", sessionId);
```

Force-destroys the session slot and frees VRAM immediately. Use when session is permanently closed.

---

## Chat Streaming

### Send Prompt

```csharp
var message = new ChatMessage("user", new List<MessageContent>
{
    new TextContent("Hello, how are you?")
});

await connection.InvokeAsync("SendPromptDelta", sessionId, message);
```

### Receive Tokens

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
            Console.Write(delta.Content); // Append token
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

Supported content types:
- `TextContent` — plain text
- `ImageFileContent` — local file path (server-side)
- `ImageBase64Content` — base64-encoded image
- `ImageUrlContent` — HTTP URL to image

---

## Context Management

### Get Token Count

```csharp
int tokenCount = await connection.InvokeAsync<int>("GetSessionTokenCount", sessionId);
```

### Rollback (Truncate KV-cache)

```csharp
await connection.InvokeAsync("RollbackSession", sessionId, targetPosition);
```

Removes all tokens after `targetPosition`. Use when user wants to "undo" last messages.

### Shift (Sliding Window)

```csharp
await connection.InvokeAsync("ShiftSessionCache", sessionId, startPos, count);
```

Removes `count` tokens starting at `startPos` and shifts remaining tokens. Frees VRAM while preserving recent context.

**Note:** Throws `NotSupportedException` if FlashAttention or GPU-offload is active (physical shift impossible). Fallback: use `RollbackSession` + re-decode.

---

## Embeddings

### Generate Embeddings

```csharp
var request = new EmbeddingRequest("Text to embed", Normalize: true);
var response = await connection.InvokeAsync<EmbeddingResponse>(
    "GenerateEmbeddingsAsync", repoId, request);

// response.Embedding — float[] vector
// response.TokenCount — tokens consumed
// response.EmbeddingDimension — vector size
```

**Prerequisites:**
- Model must be loaded with `Embeddings: true` in hardware profile.
- Model must be the active model.
- Input text must fit within context window.

**Error Cases:**
- `HubException` if gateway not ready.
- `HubException` if model not active.
- `HubException` if text exceeds context size.

---

## Model Management (Admin)

### Load Model

```csharp
await connection.InvokeAsync("LoadModelAsync", repoId, profileName);
```

Profiles are defined in `appsettings.json` under `InstantAIGate:HardwareProfiles:{profileName}`.

### Swap Model (Hot-Swap)

```csharp
await connection.InvokeAsync("SwapModelAsync", repoId, profileName);
```

Gracefully drains active requests, unloads old model, loads new model. Queue is paused during swap.

### Unload Model

```csharp
await connection.InvokeAsync("UnloadModelAsync", repoId);
```

### Get Active Model Details

```csharp
var details = await connection.InvokeAsync<NativeModelDetails>("GetActiveModelDetailsAsync");
// details.RepoId, details.ContextSize, details.GpuLayers, details.Backend, etc.
```

### Get Queue Metrics

```csharp
var metrics = await connection.InvokeAsync<InferenceMetrics>("GetQueueMetricsAsync");
// metrics.ActiveLeases, metrics.PendingRequests
```

### Set Queue Limit

```csharp
await connection.InvokeAsync("SetQueueLimitAsync", newLimit);
```

---

## Model Download

### Trigger Download

```csharp
await connection.InvokeAsync("SubscribeToModelDownload", repoId);
await connection.InvokeAsync("DownloadModelAsync", repoId);
```

### Monitor Progress

```csharp
connection.On<DownloadProgress>("ReceiveDownloadProgress", progress =>
{
    Console.WriteLine($"{progress.ModelId}: {progress.Percentage:F1}% " +
                      $"({progress.SpeedBytesPerSecond / 1048576:F2} MB/s)");
});
```

### Unsubscribe

```csharp
await connection.InvokeAsync("UnsubscribeFromModelDownload", repoId);
```

---

## VRAM Management

### Purge Idle Contexts

```csharp
await connection.InvokeAsync("PurgeIdleContextsAsync", repoId);
```

Frees all pooled idle contexts for the specified model. Use when VRAM pressure is high and no active sessions need the cached contexts.

---

## Health Checks

| Endpoint | Auth | Purpose |
|----------|------|---------|
| `/health/live` | None | Liveness probe (always 200) |
| `/health/ready` | None | Readiness probe (200 when model loaded, 503 otherwise) |

**Ready Response (200):**
```json
{ "status": "Healthy", "description": "Model 'qwen3-vl-2b-instruct' is loaded and ready." }
```

**Not Ready Response (503):**
```json
{
  "status": "Degraded",
  "description": "Downloading model 'qwen3-vl-2b-instruct' (45.5%)",
  "data": {
    "status": "ModelDownloading",
    "progressPercentage": 45.5,
    "bytesPerSecond": 25000000
  }
}
```

---

## Error Handling

### ReceiveError

General errors (model not found, session not active, etc.):
```csharp
connection.On<string>("ReceiveError", error =>
{
    Console.WriteLine($"Gateway Error: {error}");
});
```

### ReceiveContextOverflow

Context window exceeded. Client should offer mitigation:
```csharp
connection.On<ContextOverflowException>("ReceiveContextOverflow", ex =>
{
    // ex.PastTokens, ex.IncomingTokens, ex.ReservedTokens, ex.ContextSize
    // Options: ShiftSessionCache, RollbackSession, or clear session
});
```

**Recommended Mitigation Flow:**
1. Try `ShiftSessionCache` (sliding window).
2. If `NotSupportedException` → `RollbackSession` to safe position.
3. If user wants fresh start → `DestroySession` + new `JoinSession`.

### HTTP Error Codes (Middleware)

| Code | Exception | Meaning |
|------|-----------|---------|
| 429 | `QueueFullException` | Gateway at capacity |
| 504 | `TimeoutException` | Queue wait timeout |
| 503 | `InvalidOperationException` (draining) | Model swapping |
| 500 | Other | Internal error |

---

## Ephemeral Sessions

Ephemeral sessions are automatically destroyed when the client disconnects. Use for CLI/terminal sessions where no reconnect is expected.

### Mark as Ephemeral

```csharp
await connection.InvokeAsync("JoinSession", sessionId, repoId);
await connection.InvokeAsync("MarkSessionAsEphemeral", sessionId);
```

**Behavior on Disconnect:**
- All ephemeral sessions for the disconnected connection are force-destroyed (`destroySlot: true`).
- VRAM is immediately freed.
- No manual cleanup needed.

**Non-Ephemeral Sessions:**
- Preserved on disconnect for potential reconnect.
- Automatically cleaned up after idle timeout (10 minutes).
- Orphaned sessions (no connection) are reaped after 2 minutes by `SessionReaperService`.

---

## Complete Integration Example

```csharp
using Microsoft.AspNetCore.SignalR.Client;

var connection = new HubConnectionBuilder()
    .WithUrl("http://localhost:5000/hub/gateway", options =>
    {
        options.AccessTokenProvider = () => Task.FromResult("your-api-key")!;
    })
    .WithAutomaticReconnect()
    .Build();

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
await connection.InvokeAsync("JoinSession", sessionId, "");
await connection.InvokeAsync("MarkSessionAsEphemeral", sessionId);

// Send prompt
var message = new ChatMessage("user", "Hello!");
await connection.InvokeAsync("SendPromptDelta", sessionId, message);

// Wait for completion (use TaskCompletionSource in production)
await Task.Delay(10000);

// Cleanup
await connection.InvokeAsync("LeaveSession", sessionId);
await connection.StopAsync();
```

---

## Configuration Reference

### Hardware Profiles (appsettings.json)

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
    "AdminApiKey": "your-secret-admin-key"
  }
}
```

### VRAM Budgeting Guidelines

| Model Size | KvCacheQuantization | MaxContexts | FlashAttention |
|------------|---------------------|-------------|----------------|
| ≤ 8B | F16 | 4 | true |
| 8B–14B | Q8_0 | 3 | true |
| 14B–32B | Q8_0 | 2 | true |
| > 32B | Q4_K | 1 | false |

**Critical:** `FlashAttention: true` disables `ShiftSessionCache`. Use `RollbackSession` as fallback.