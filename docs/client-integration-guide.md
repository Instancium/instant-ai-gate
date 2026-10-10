# InstantAIGate Client Integration Guide
## SignalR Hub Protocol & Session Memory Model Reference
**Contract Version:** 1.0 | **Server Target Platform:** .NET 10 | **Transport:** SignalR (WebSockets / Server-Sent Events / Long Polling via negotiate)

### 1. Purpose and Scope of the Document
This document describes the complete wire contract of the InstantAIGate gateway: authentication, hub methods (client → server), callbacks (server → client), DTO schemas, the session memory model (KV-cache, VRAM slots, ephemeral sessions), and canonical client patterns. A client application can be implemented exclusively based on this document. Method names, argument order, and DTO schemas constitute a stable contract: additions are allowed, but deletions and renames are breaking changes.

Typed DTOs are available via the NuGet package `InstantAIGate.Core` (namespaces `InstantAIGate.Core.Dtos.*`). The client may choose not to reference the package and serialize DTOs independently according to the schemas in Section 6.

### 2. Architectural Model
The server is a single ASP.NET Core process (Kestrel or Windows service `InstantAIGate.Server`) with a single SignalR endpoint: `/hub/gateway`.
Inference is performed by the native `llama.cpp` engine; only one loaded model is active at a time (hot-swapping between models is supported).
**Memory:** Model weights reside in VRAM (with GPU-offload) + mmap mirror; each session owns a KV-context (`llama_context`) of 256–512 MB VRAM depending on the context configuration. The number of concurrent sessions is limited by available VRAM, not by a license/counter.
**Context Pool:** Released "warm" contexts are returned to the model pool and reused by new sessions with a completely cleared KV-cache. The pool saves allocations but does not preserve history continuity.

### 3. Transport, Authentication, Roles
| Parameter | Value |
| --- | --- |
| Hub URL | `{baseUrl}/hub/gateway` |
| Token | HTTP header `Authorization: Bearer <key>` OR raw header `Authorization: <key>` OR query parameter `access_token=<key>` (for WebSocket clients without headers) |
| Admin Key | Value of the server setting `InstantAIGate:AdminApiKey`; grants roles `Admin` + `User`, `TenantId=admin` |
| Any other key | Role `User`, `TenantId=<key>` |
| Anonymous access | Only `/health/live`, `/health/ready` |
| HTTP health endpoints | `/health/live` → 200 always; `/health/ready` → 200 (model ready) / 503 (downloading/loading/faulted/no model) |

Hub method authorization policies: `GatewayUser` (User + Admin roles) and `GatewayAdmin` (Admin only). Calling a method with the wrong policy results in a `HubException` on the client side.

### 4. Hub Contract: Client → Server Methods
Arguments are listed in wire order. Methods returning `Task` do not yield return values; business-level errors are transmitted via the `ReceiveError` callback, not via exceptions (exceptions are only thrown for policy violations and argument validation failures).

#### 4.1 Data Plane (GatewayUser)
| Method | Signature | Semantics and Errors |
| --- | --- | --- |
| `JoinSession` | `(string sessionId, string repoId)` | Creates a session and binds it to the connection. `repoId == ""` → use the active model; otherwise, `repoId` must match the active model. Obstacles (model downloading/loading/faulted/no active model/mismatch) → `ReceiveError` with the reason text, session is NOT created. Success is silent (no callback log). |
| `MarkSessionAsEphemeral` | `(string sessionId)` | Declares the session as ephemeral: upon physical connection drop, the server guarantees destruction of its KV-slot (`destroySlot: true`) regardless of the client's state. Call immediately after a successful `JoinSession` for all pipeline sessions. |
| `LeaveSession` | `(string sessionId)` | Graceful termination: session is deleted, KV-context returns to the pool (VRAM is NOT freed). Callback: `ReceiveSessionClosed`. |
| `DestroySession` | `(string sessionId)` | Hard termination: KV-slot is freed from VRAM immediately. Callback: `ReceiveSessionClosed`. Mandatory for pipeline sessions. |
| `SendPromptDelta` | `(string sessionId, ChatMessage deltaMessage)` | Starts inference; tokens arrive via `ReceiveTokenDelta` callback; completion is a delta with `IsDone=true`, `FinishReason="stop"`. Context overflow → `ReceiveContextOverflow`; failures → `ReceiveError`. Returning `Task` indicates the end of the stream. |
| `RollbackSession` | `(string sessionId, int targetPosition)` | Truncates the KV-cache up to the token position (suffix is deleted). Errors → `ReceiveError`. |
| `ShiftSessionCache` | `(string sessionId, int startPos, int count)` | Sliding window: deletes a KV range and shifts the tail. May be unavailable: `ReceiveError` with text containing `FlashAttention or GPU offload` → client must fall back to `RollbackSession`. |
| `GetSessionTokenCount` | `(string sessionId) → int` | Current length of the session's KV-cache in tokens (baseline for checkpoints). |

#### 4.2 Observability (GatewayUser)
| Method | Signature | Semantics |
| --- | --- | --- |
| `GetGatewayStatus` | `() → GatewayStatusDetails` | Instant snapshot of the gateway state. |
| `SubscribeToModelDownload` | `(string repoId)` | Subscribes to the `download_{repoId}` progress group. |
| `UnsubscribeFromModelDownload` | `(string repoId)` | Unsubscribes. |

#### 4.3 Control Plane (GatewayAdmin)
| Method | Signature | Semantics and Important Restrictions |
| --- | --- | --- |
| `GetActiveModelDetailsAsync` | `() → NativeModelDetails` | `GatewayUser` policy (available to all). Note: `GpuLayers` is an echo of the configuration, not measured placement. |
| `GetModelsAsync` | `() → ModelRegistryStatus[]` | Registry of loaded models and idle contexts. |
| `LoadModelAsync` | `(string repoId, string? profile)` | Loads a model. If a model with the same `repoId` is already active → no-op (early exit). This is not a reload. |
| `SwapModelAsync` | `(string repoId, string? profile)` | Graceful hot-swap: drain active leases → unload → reload (including the same `repoId`). The only standard way to reallocate weights in currently free VRAM ("model reload"). |
| `UnloadModelAsync` | `(string repoId)` | Unloads the model; all context pools are freed. |
| `PurgeIdleContextsAsync` | `(string repoId)` | Frees only idle contexts in the pool. Does not touch live session contexts and does not reallocate weights. |
| `DownloadModelAsync` | `(string repoId)` | Queues background download from the directory; progress via broadcast groups. |
| `GetQueueMetricsAsync` | `() → InferenceMetrics` | Queue/lease metrics. |
| `SetQueueLimitAsync` | `(int limit)` | `limit <= 0` → `HubException`. |

### 5. Hub Contract: Server → Client Callbacks
| Callback | Payload | Purpose |
| --- | --- | --- |
| `ReceiveTokenDelta` | `SessionTokenDelta` | Token stream; final delta has `IsDone=true`. |
| `ReceiveError` | `string` | Business errors for sessions and preprocessing. |
| `ReceiveContextOverflow` | `ContextOverflowException` | Structured context overflow (see 7.4). |
| `ReceiveSessionClosed` | `string sessionId` | Confirmation of Leave/Destroy. |
| `ReceiveDownloadProgress` | `DownloadProgress` | Download progress (`download_*` groups and Admin). |
| `ReceiveQueuePosition` | `int` | Position in the inference queue (`>0`). |
| `ReceiveLog` | `(string level, string category, string message)` | Server logs (Admin group only). |
| `ReceiveMetrics` | `(InferenceMetrics, object nativeDetails)` | Reactive metrics (Admin group only). |
| `ReceiveGatewayStatus` | `GatewayStatusDetails` | Status snapshot: upon connection (caller), upon state change (all). |

Upon connection, the caller always receives `ReceiveGatewayStatus`; Admin connections additionally receive `ReceiveMetrics` and join the `GatewayAdminGroup`.

### 6. DTO Schemas (System.Text.Json, camelCase)
*   `SessionTokenDelta { sessionId, content, isDone=false, finishReason=null }`
*   `ChatMessage { role, parts[], content? }` ; `parts` is a polymorphic array of `MessageContent` with a `"type"` discriminator: `text { text }`, `image_base64 { base64, mediaType }`, `image_url { url }`, `image_file { filePath }`. `image_file` accepts a path local to the SERVER; remote clients must use `image_url` or `image_base64`.
*   `GatewayStatusDetails { status, activeModelId?, stageDescription?, progressPercentage, downloadedBytes, totalBytes, bytesPerSecond, errorMessage?, timestamp }`; `status`: `0 Uninitialized, 1 ModelDownloading, 2 ModelLoading, 3 Ready, 4 Faulted`.
*   `NativeModelDetails { repoId, contextSize, gpuLayers, totalLayers, threads, flashAttention, idleContextsCount, backend }`
*   `ModelRegistryStatus { repoId, isLoaded, idleContextsCount, maxContexts, gpuLayers, type }`
*   `InferenceMetrics { activeLeases, pendingRequests }`
*   `DownloadProgress { modelId, bytesDownloaded, totalBytes, speedBytesPerSecond, percentage }`
*   `ContextOverflowException { sessionId, pastTokens, incomingTokens, reservedTokens, contextSize }`

### 7. Session Memory Model (Mandatory Understanding)
#### 7.1 KV-Slot Lifecycle
| Event | Session Fate | KV-Context Fate (VRAM) |
| --- | --- | --- |
| `LeaveSession` | Deleted | To pool (warm, KV cleared) — VRAM occupied |
| `DestroySession` | Deleted | Freed immediately |
| Connection drop, regular session | Deleted (`destroySlot:false`) | To pool — VRAM occupied until purge/unload |
| Connection drop, ephemeral session | Deleted (`destroySlot:true`) | Freed immediately (< 2s to DoD) |
| Idle > 10 mins (server timer) | Deleted (`destroySlot:false`) | To pool — VRAM occupied |
| `PurgeIdleContextsAsync` | Sessions unaffected | Idle pool contexts freed |

**Takeaway for the client:** The only ways to guarantee VRAM is returned to the machine are `DestroySession` or an ephemeral declaration + connection drop.

#### 7.2 Ephemeral Sessions and Reaper
`MarkSessionAsEphemeral` binds the `sessionId` to the `ConnectionId` in an orthogonal registry; the server's `OnDisconnectedAsync` destroys such sessions with `destroySlot:true`.
The background `SessionReaperService` (30s interval) forcefully destroys ephemeral sessions registered before the orphaning threshold. In the current build, the threshold is hardcoded to 2 minutes and is not refreshed by activity (known limitation, see Section 10): an ephemeral session older than the threshold will be destroyed even if the connection is alive. Clients with long pipelines must handle the subsequent "session not active" error by re-joining (see 8).

#### 7.3 History Continuity
The KV-cache does not survive connection drops or Leave/Idle evacuations: the pool issues contexts with cleared KV. The source of truth for the dialogue is the client; the server's session state = KV + token counter, recoverable only by re-running the history.

#### 7.4 Context Overflow
When `pastTokens + incoming + reserve > contextSize`, the stream is interrupted by `ReceiveContextOverflow`. Client mitigation menu: (1) `ShiftSessionCache` (sliding window; may be unavailable), (2) `RollbackSession` to a checkpoint, (3) `DestroySession` + recreate, (4) cancel request.

### 8. Canonical Client Patterns
#### 8.1 Interactive Chat
`JoinSession` → (optional `MarkSessionAsEphemeral` not needed for short sessions) → loop `SendPromptDelta`/`ReceiveTokenDelta` → upon dialogue completion `DestroySession` (not `LeaveSession`, if the dialogue is not planned to be continued).

#### 8.2 Pipeline Client (Map-Reduce, Checkpoints) — Reference for InstantPublisher
1. `JoinSession(pipelineSessionId, repoId)`.
2. `MarkSessionAsEphemeral(pipelineSessionId)` — immediately after Join.
3. System prompt via `SendPromptDelta`; fix checkpoint: `sysCheckpoint = GetSessionTokenCount(...)`.
4. For each chunk: `SendPromptDelta(chunk)` → collect stream → `RollbackSession(pipelineSessionId, sysCheckpoint)` (KV doesn't grow, system prefix is reused).
5. Reduce phase is similar to chunks.
6. In `finally`: best-effort `DestroySession(pipelineSessionId)` — attempt even in Reconnecting state, do not propagate errors (server ephemeral guarantee acts as insurance).
7. Stream error handling: texts containing `not active` / `was not found` (including from the reaper, see 7.2) → single retry with re-`JoinSession` + `MarkSessionAsEphemeral` before emitting the first chunk token.

#### 8.3 Reconnect
The server saves nothing for a reconnected connection: all sessions of the old `ConnectionId` are already released/destroyed in `OnDisconnectedAsync`. A client with `WithAutomaticReconnect()` must, upon `Reconnected`: clear the local registry of joined sessions, re-execute `JoinSession` + `MarkSessionAsEphemeral` for required sessions, and restore KV by running history or checkpoints. Streams interrupted by a drop are not resumed by the server.

#### 8.4 Admin Client: Model Management
Loading: `LoadModelAsync`; reloading/reallocation: only `SwapModelAsync(repoId, null)` (Load with the same `repoId` is a no-op); unloading: `UnloadModelAsync`; emergency pool freeing: `PurgeIdleContextsAsync`. After operations, monitor `ReceiveGatewayStatus` until `Ready`.

### 9. Streams and Timeouts Rules
*   One `SendPromptDelta` per session at a time: the server serializes inference via the session's execution gate; parallel calls do not error out but queue up — the client should not rely on this as parallelism.
*   Client timeout recommendations: chunk stream ≤ 60s per 1k generated tokens; `JoinSession` ≤ 10s; `Swap`/`Load` — no client timeout (long operations, monitor via status).
*   Cancellation: the server stream honors the connection's CancellationToken; client cancellation = drop/SignalR token, after which the session stays alive (KV preserved) — explicitly Rollback/Destroy if necessary.

### 10. Known Limitations of the Current Build (Errata)
*   The ephemeral session reaper uses the registration age (2 min threshold) without refreshing on activity: long ephemeral pipelines are subject to forced destruction; the client must have retry logic (8.2). Server follow-up: refresh the activity timestamp.
*   `ShiftSessionCache` is unavailable with GPU-offload/FlashAttention (NotSupportedException text) — plan the rollback path as the primary one.
*   `NativeModelDetails.GpuLayers` is a configuration echo; actual layer placement is visible only in native server logs.
*   `image_file` is a server-local path; forbidden for remote clients.
*   The context pool is not capped by the `MaxContexts` value (the field is informational); VRAM discipline is the client's responsibility (Destroy/ephemeral).

### 11. Client Readiness Checklist (Definition of Done)
- [ ] Token is passed via `access_token`/Authorization; 401 on negotiate is handled.
- [ ] All pipeline sessions: Join → MarkSessionAsEphemeral → Destroy in finally (best-effort).
- [ ] Retry on `not active`/`was not found` with re-Join before the first token.
- [ ] Reconnect-hook recreates sessions and ephemeral declarations.
- [ ] `ReceiveContextOverflow` (rollback path) and `ReceiveError` (logged, doesn't crash UI) are handled.
- [ ] Admin functions use Swap for reloading; Load is not used as reload.
- [ ] No session leaks: audit of active sessionIds upon client process termination is empty.

### 12. Glossary
*   **KV-slot** — allocated `llama_context` of a session (256–512 MB VRAM).
*   **Ephemeral session** — a session with a server guarantee of `destroySlot:true` upon connection drop.
*   **Checkpoint** — token position up to which `RollbackSession` is executed to reuse the prefix.
*   **Pool** — a set of warm model contexts with cleared KV.