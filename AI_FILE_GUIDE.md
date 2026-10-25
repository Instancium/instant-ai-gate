# AI File Guide — InstantAIGate Architecture Map

**Document Version:** 1.3
**Last Updated:** Reflects codebase state with embedding support, ephemeral session management, gateway state machine, and VRAM purging.

This file describes the key modules of the repository. Use it as a navigation map when analyzing or generating code.

---

## Server Entry Point

**File:** `src/InstantAIGate.Server/Program.cs`

**Role:** ASP.NET Core application entry point. Registers DI container, middleware, SignalR hubs, hosted services, and health checks.

**Key Dependencies:**
- `InstantAIGate.Native.DependencyInjection.ServiceCollectionExtensions` — registers native stack (`BackendFacade`, `LlamaInference`, `ModelManager`).
- `InstantAIGate.SSR.DependencyInjection.ServiceCollectionExtensions` — registers model downloader and catalog.
- `InstantAIGate.Server.Hubs.GatewayHub` — central SignalR hub.
- `InstantAIGate.Server.Services.Workers.*` — background tasks (`ModelStartupWorker`, `ModelDownloadWorker`, `MetricsBroadcasterWorker`).
- `InstantAIGate.Core.Interfaces.Session.IEphemeralSessionRegistry` — ephemeral session tracking.
- `InstantAIGate.Core.Interfaces.Session.ISessionReaperService` — orphaned session cleanup.

**Invariants:**
- Health check `/health/live` returns 200 without authorization (for orchestrators).
- Health check `/health/ready` returns 503 until model is loaded and status is `Ready`.
- SignalR hub is mapped to `/hub/gateway`.
- `ApiKeyAuthMiddleware` authenticates all non-health requests.
- `GatewayExceptionMiddleware` catches `QueueFullException`, `TimeoutException`, and general exceptions.

**Scenarios:**
- Running server as Windows service (`UseWindowsService`).
- Intercepting native llama.cpp logs via `NativeStreamRedirector`.
- Broadcasting gateway status changes to SignalR clients.

---

## Central SignalR Hub

**File:** `src/InstantAIGate.Server/Hubs/GatewayHub.cs`

**Role:** Single entry point for data plane (inference) and control plane (model management). Handles authorization, queues, sessions, and token streaming.

**Key Dependencies:**
- `ISessionInferenceManager` — session state and KV-cache management.
- `IInferenceEngine` — token generation delegation.
- `IModelManager` — model loading/unloading/purging.
- `IQueueManager` — concurrency control.
- `IEphemeralSessionRegistry` — session registration with auto-deletion on disconnect.
- `IGatewayStateManager` — gateway operational status tracking.
- `ChannelWriter<DownloadJob>` — background download queue.

**Invariants:**
- All public methods require `[Authorize]`.
- Data plane methods (`SendPromptDelta`, `JoinSession`, `GetSessionTokenCount`, `GetGatewayStatus`) require `GatewayUser` role.
- Control plane methods (`LoadModelAsync`, `SwapModelAsync`, `UnloadModelAsync`, `DownloadModelAsync`, `PurgeIdleContextsAsync`, `SetQueueLimitAsync`) require `GatewayAdmin` role.
- On disconnect, all registered sessions are released; ephemeral sessions are deleted with `destroySlot: true`.
- `JoinSession` rejects requests if gateway is in `ModelDownloading`, `ModelLoading`, or `Faulted` state.

**Hub Methods (Data Plane — GatewayUser):**
| Method | Description |
|--------|-------------|
| `JoinSession(sessionId, repoId)` | Creates session, binds to connection |
| `LeaveSession(sessionId)` | Releases session, notifies client |
| `DestroySession(sessionId)` | Force-destroys session slot |
| `MarkSessionAsEphemeral(sessionId)` | Registers session for auto-cleanup |
| `SendPromptDelta(sessionId, deltaMessage)` | Streams inference tokens |
| `RollbackSession(sessionId, targetPosition)` | Truncates KV-cache |
| `ShiftSessionCache(sessionId, startPos, count)` | Sliding window eviction |
| `GetSessionTokenCount(sessionId)` | Returns current PastTokens |
| `GetGatewayStatus()` | Returns current gateway status snapshot |
| `SubscribeToModelDownload(repoId)` | Joins download progress group |
| `UnsubscribeFromModelDownload(repoId)` | Leaves download progress group |
| `GenerateEmbeddingsAsync(repoId, request)` | Generates vector embeddings |

**Hub Methods (Control Plane — GatewayAdmin):**
| Method | Description |
|--------|-------------|
| `LoadModelAsync(repoId, profile)` | Loads model with hardware profile |
| `SwapModelAsync(repoId, profile)` | Hot-swaps active model |
| `UnloadModelAsync(repoId)` | Unloads model, resets state |
| `DownloadModelAsync(repoId)` | Enqueues background download |
| `PurgeIdleContextsAsync(repoId)` | Frees pooled idle contexts |
| `SetQueueLimitAsync(limit)` | Updates dynamic queue capacity |
| `GetModelsAsync()` | Lists active model registry |
| `GetQueueMetricsAsync()` | Returns queue metrics |

---

## Hub Client Contract

**File:** `src/InstantAIGate.Server/Hubs/IGatewayHubClient.cs`

**Role:** Interface describing all methods the server calls on the client (push notifications).

**Methods:**
| Method | Description |
|--------|-------------|
| `ReceiveTokenDelta(SessionTokenDelta)` | Generated text chunk |
| `ReceiveContextOverflow(ContextOverflowException)` | Context window exceeded |
| `ReceiveError(string)` | General error |
| `ReceiveSessionClosed(string)` | Session closed |
| `ReceiveDownloadProgress(DownloadProgress)` | Model download progress |
| `ReceiveQueuePosition(int)` | Inference queue position (Reserved) |
| `ReceiveLog(string, string, string)` | Server logs (admin only) |
| `ReceiveMetrics(InferenceMetrics, object)` | Inference metrics (admin only) |
| `ReceiveGatewayStatus(GatewayStatusDetails)` | Gateway operational status |

---

## Gateway State Manager

**File:** `src/InstantAIGate.Core/Services/Inference/GatewayStateManager.cs`

**Role:** Stores current gateway operational status and publishes changes via `StatusChanged` event. Thread-safe state machine.

**State Transitions:**
```
Uninitialized → ModelDownloading → ModelLoading → Ready
Any State → Faulted
Ready → Uninitialized (via Reset/Unload)
```

**Key Concepts:**
- Thread-safe via `lock (_syncLock)`.
- `UpdateAndPublish` — atomically updates state and invokes all subscribers.
- Exceptions in subscribers are swallowed (do not block other observers).

**Invariants:**
- `SetFaulted` concatenates `errorMessage` and `exception.Message`.
- `Reset` returns status to `Uninitialized`.
- `SetDownloading` clamps `progressPercentage` to [0, 100].

**Scenarios:**
- `ModelStartupWorker` updates status during startup model loading.
- `MetricsBroadcasterWorker` broadcasts status to admin SignalR group.
- Health check `/health/ready` reads snapshot to determine degraded/unhealthy.

---

## Session State Manager

**File:** `src/InstantAIGate.Core/Services/Inference/SessionInferenceManager.cs`

**Role:** Stores state of all active sessions: token counter, token sequence, reference to `InferenceContext` (KV-cache). Ensures thread-safe access via `SemaphoreSlim` gates.

**Key Concepts:**
- `StatefulSessionEntry` — internal session record with `PastTokens`, `TokenSequence`, `ExecutionGate`, `ContextInitGate`.
- `AcquireSessionExecutionGateAsync` — blocks concurrent generation in the same session.
- `RollbackToPositionAsync` — truncates KV-cache to specified position via `IBackendFacade.RemoveContextMemoryRange`.
- `ShiftMemoryRangeAsync` — shifts KV-cache (sliding window) if `CanShiftContextMemory == true`.
- `UpdatePastTokensCount` / `AppendSessionTokens` / `GetSessionPrefixTokens` — token tracking.

**Invariants:**
- `RollbackToPositionAsync` throws `ArgumentOutOfRangeException` if `targetPosition > PastTokens`.
- `ShiftMemoryRangeAsync` throws `NotSupportedException` if FlashAttention or GPU-offload prohibits shifting.
- Idle sessions are automatically released by timer (default 10 minutes).
- `ReleaseSessionAsync(destroySlot: true)` sets `SuppressPool = true` on context to prevent VRAM pooling.

**Scenarios:**
- CLI command `/ctx rollback <pos>`.
- CLI command `/ctx shift <start> <count>`.
- Handling `ContextOverflowException` in `LlamaInference`.

---

## Ephemeral Session Registry & Reaper

**File:** `src/InstantAIGate.Core/Services/Inference/Session/EphemeralSessionRegistry.cs`
**File:** `src/InstantAIGate.Core/Services/Inference/Session/SessionReaperService.cs`

**Role:** Tracks sessions marked as ephemeral and associates them with `ConnectionId`. On disconnect, returns session list for forced deletion. The reaper periodically drains orphaned sessions.

**Key Concepts:**
- `_connectionToSessions` — mapping ConnectionId → HashSet<SessionId>.
- `_sessionRegistrationTime` — registration timestamp for orphan detection.
- `DrainOrphanedSessions` — finds sessions older than threshold (2 minutes) without active connection.
- `SessionReaperService` runs every 30 seconds, calls `DrainOrphanedSessions`, force-releases orphans with `destroySlot: true`.

**Invariants:**
- `NotifyConnectionDisconnected` deletes all connection sessions and returns their list.
- `UnregisterSession` removes session from all connections.
- Orphan threshold is 2 minutes.

**Scenarios:**
- CLI sessions are ephemeral by default (auto-cleanup on terminal close).
- Hard disconnect detection via `IConnectionLifetimeFeature.Abort()`.
- VRAM leak prevention.

---

## Model Lifecycle Manager

**File:** `src/InstantAIGate.Core/Services/Inference/ModelManager.cs`

**Role:** Manages model lifecycle: loading, hot-swap, unloading, context purging. Coordinates `IModelProvider` and `IQueueManager`.

**Key Concepts:**
- `_globalLock` (SemaphoreSlim) — prevents parallel load/unload operations.
- `_activeLeases` (Interlocked) — counter of active inference operations.
- Graceful swap: pause queue → wait for active leases to complete → unload old model → load new model → resume queue.
- `PurgeIdleContextsAsync` — delegates to `IModelProvider.PurgeIdleContexts` to free pooled VRAM contexts.

**Invariants:**
- `AcquireContextAsync` throws `InvalidOperationException` if model is not active or draining.
- `SwapModelAsync` waits until `_activeLeases == 0` before unloading old model.
- `PurgeIdleContextsAsync` is a synchronous operation (no async wait needed).

**Scenarios:**
- Admin command `LoadModelAsync` in `GatewayHub`.
- Admin command `SwapModelAsync` in `GatewayHub`.
- CLI command `/vram purge` → `PurgeIdleContextsAsync`.
- Automatic startup model loading via `ModelStartupWorker`.

---

## Request Concurrency Control

**File:** `src/InstantAIGate.Core/Services/Inference/DynamicRequestQueue.cs`

**Role:** Limits concurrent inference requests. Implements backpressure via `SemaphoreSlim` and dynamic limit adjustment.

**Key Concepts:**
- `EnqueueRequestAsync` — atomically increments `_pendingCount`, waits for semaphore slot, returns `IQueueLease`.
- `UpdateQueueLimit` — dynamically changes `_maxQueueSize` (release/acquire slots).
- `Pause/Resume` — flag `_isPaused` blocks new slot issuance (used during model hot-swap).
- `QueueLease.Dispose()` releases semaphore slot and decrements pending count.

**Invariants:**
- `QueueFullException` thrown if `PendingCount >= MaxQueueSize`.
- `TimeoutException` thrown if slot not acquired within `timeout`.
- On token cancellation, `_pendingCount` is decremented.
- `IMetricsEventSource.NotifyStateChanged()` is called on every state mutation.

**Scenarios:**
- Gateway-level rate limiting.
- Admin command `SetQueueLimitAsync`.
- Queue pause during `SwapModelAsync`.

---

## Native Inference Engine

**File:** `src/InstantAIGate.Native/Inference/LlamaInference.cs`

**Role:** Primary inference engine. Converts `ChatMessage` → tokens → llama_decode → token streaming. Works directly with native handles via unsafe code.

**Key Dependencies:**
- `IBackendFacade` — abstraction over P/Invoke.
- `ISessionInferenceManager` — session state management.
- `IModelManager` — obtaining `ModelWeights` and `InferenceContext`.
- `IMediaResolver` — image processing for VLM.

**Key Methods:**
| Method | Description |
|--------|-------------|
| `TokenizeDataAsync` | Converts text to token IDs |
| `ApplyChatTemplateAsync` | Applies chat template to messages |
| `StreamDeltaGenerationAsync` | Main inference loop with token streaming |
| `GenerateEmbeddingsAsync` | Generates normalized vector embeddings |

**Invariants:**
- Throws `ContextOverflowException` if `PastTokens + EstimatedTokens + Reserve > ContextSize`.
- Removes BOS token from delta if `PastTokens > 0`.
- Uses unsafe code for direct `llama_batch` population.
- UTF-8 decoding via `Decoder.GetChars` with flush at end.
- Embedding generation requires `ModelSettings.Embeddings == true`.
- Embedding normalization (L2) is optional via `EmbeddingRequest.Normalize`.

**Scenarios:**
- Token streaming via `StreamDeltaGenerationAsync`.
- Processing multimodal requests (images via `mtmd_tokenize`).
- Tokenization and chat-template application.
- Embedding generation for RAG pipelines.

---

## Native Backend Facade

**File:** `src/InstantAIGate.Native/Inference/BackendFacade.cs`

**Role:** Facade over P/Invoke bindings for `llama.cpp`. Converts managed objects (`ModelSettings`) to native parameters and wraps native pointers in `IModelHandle`/`IContextHandle`.

**Key Methods:**
| Method | Native Call |
|--------|-------------|
| `LoadModel` | `llama_model_load_from_file` |
| `CreateContext` | `llama_init_from_model` with TypeK/TypeV config |
| `RemoveContextMemoryRange` | `llama_memory_seq_rm` |
| `ShiftContextMemoryRange` | `llama_memory_seq_add` |
| `CanShiftContextMemory` | `llama_memory_can_shift` (false with FlashAttention) |
| `ClearContextMemory` | `llama_memory_clear` |
| `GetEmbeddings` | `llama_get_embeddings_seq` |
| `GetModelLayerCount` | `llama_model_n_layer` |

**Invariants:**
- `KvCacheQuantization` maps to `GgmlType` (F16=2 bytes, Q8_0=1 byte, Q4_K=0.5 byte).
- `FlashAttention` disables KV-cache shifting capability.
- `SetLogCallback` routes native logs to managed `ILogger`.

**Scenarios:**
- Creating context with specified size and quantization.
- Sliding window operations on KV-cache.
- Extracting embeddings after decode.

---

## Model Validator

**File:** `src/InstantAIGate.Native/Inference/NativeModelValidator.cs`

**Role:** Validates GGUF model file integrity using native llama.cpp library.

**Key Concepts:**
- Loads model with `VocabOnly = true` and `NoAlloc = true` to read headers without allocating tensors.
- Checks if model handle is successfully created.
- Logs validation result.

**Invariants:**
- Requires `NativeLibraryLoader.IsLoaded` before validation.
- On validation failure in `ParallelModelDownloader`, temporary files are deleted.
- Returns `false` (never throws) for invalid files.

**Scenarios:**
- Post-download validation in `ParallelModelDownloader`.
- Pre-load integrity check.

---

## Background Workers

### ModelStartupWorker

**File:** `src/InstantAIGate.Server/Services/Workers/ModelStartupWorker.cs`

**Role:** Automatically loads a configured startup model on server boot. Downloads if missing, then loads into VRAM.

**Configuration:**
```json
{
  "InstantAIGate": {
    "StartupModel": {
      "Enabled": true,
      "RepoId": "qwen3-vl-2b-instruct",
      "Profile": "Default"
    }
  }
}
```

**Flow:**
1. Check if model exists on disk via `IModelLocator.ResolvePathsAsync`.
2. If missing → download via `IModelDownloader` with progress broadcasting.
3. Load model via `IModelManager.LoadModelAsync`.
4. Update `IGatewayStateManager` at each stage.

### ModelDownloadWorker

**File:** `src/InstantAIGate.Server/Services/Workers/ModelDownloadWorker.cs`

**Role:** Processes background download jobs from `ChannelReader<DownloadJob>`. Broadcasts progress to SignalR groups.

**Key Concepts:**
- Downloads are queued via `ChannelWriter<DownloadJob>` from `GatewayHub.DownloadModelAsync`.
- Progress is broadcast to `download_{modelId}` group and `GatewayAdminGroup`.
- Uses scoped `IServiceProvider` for transient downloader resolution.

### MetricsBroadcasterWorker

**File:** `src/InstantAIGate.Server/Diagnostics/MetricsBroadcasterWorker.cs`

**Role:** Reactive metrics broadcaster. Listens to `IMetricsEventSource.Reader` channel and pushes metrics to admin group on state change.

**Invariants:**
- Only broadcasts on event (no polling).
- Status changes are throttled to 1 second intervals.
- Subscribes/unsubscribes to `IGatewayStateManager.StatusChanged`.

---

## Parallel Model Downloader

**File:** `src/InstantAIGate.SSR/Downloader/ParallelModelDownloader.cs`

**Role:** Downloads GGUF model files with range request support, resume capability, parallel download (4 threads), and integrity validation.

**Key Concepts:**
- `DownloadManifest` — JSON file `.meta.json` for storing chunk progress.
- `DownloadParallelAsync` — splits file into 4 chunks, downloads in parallel via `Range` header.
- `DownloadSequentialAsync` — fallback for servers without range support.
- After download, calls `IModelValidator.ValidateIntegrityAsync` (native GGUF header check).

**Invariants:**
- Minimum size for parallel download: 10 MB.
- On cancellation, progress saved in manifest for resume.
- On validation failure, temporary files deleted and `ModelIntegrityException` thrown.
- Supports both HTTP Range and non-Range servers.

**Scenarios:**
- Startup model loading via `ModelStartupWorker`.
- Model download on admin request via `GatewayHub.DownloadModelAsync`.
- CLI command `/download <modelId>`.

---

## CLI Entry Point

**File:** `src/InstantAIGate.Cli/Program.cs`

**Role:** Interactive CLI client entry point. Configures DI, registers commands, creates `GatewayClientProxy` (local or remote mode).

**Key Dependencies:**
- `CommandDispatcher` — routes commands by `/` prefix.
- `GatewayClientProxy` — proxy between `LocalGatewayClient` and `RemoteGatewayClient` for seamless local/remote switching.
- `CliHostedService` — main REPL loop.
- `ClientSessionMemoryCoordinator` — orchestrates context operations (shift/rollback/evict) for the CLI.

**Invariants:**
- Operating mode determined by `--remote [baseUrl] [--key apiKey]` flag.
- Local mode uses direct DI container with native stack.
- Remote mode connects via SignalR.
- `DebugState` controls native log visibility (`/debug on|off`).

---

## CLI Commands Reference

**Directory:** `src/InstantAIGate.Cli/Commands/`

| Command | Role |
|---------|------|
| `/load <modelId> [profile]` | Load model into VRAM |
| `/ctx info\|shift\|rollback\|clear` | Context management |
| `/image <path>` | Attach image for next prompt |
| `/connect [url] [key]` or `/connect local` | Switch remote/local mode |
| `/vram` | Display backend configuration |
| `/vram purge` | Purge idle VRAM context pool |
| `/download <modelId>` | Download model from catalog |
| `/models` | List available models from catalog |
| `/embed <text>` | Generate embeddings for text |
| `/debug on\|off` | Toggle native diagnostic logs |
| `/help` | Display command reference |

---

## Native Library Loader

**File:** `src/InstantAIGate.Native/Bindings/NativeLibraryLoader.cs`

**Role:** Loads native libraries `llama.dll`/`libllama.so` and `mtmd.dll`/`libmtmd.so` via `NativeLibrary.SetDllImportResolver`.

**Key Concepts:**
- Searches for libraries in `AppDomain.CurrentDomain.BaseDirectory` or custom directory.
- Calls `llama_backend_init()` after successful loading.
- `Unload()` releases libraries via `NativeLibrary.Free`.

**Invariants:**
- Loading is idempotent (protected by `LockObj`).
- `IsLoaded` — public flag for native stack readiness.

---

## Native Library Binaries

**Directory:** `runtimes/`

**Contents:**
- **Windows:** `llama.dll`, `mtmd.dll` (and dependencies) in `runtimes/win-x64/`.
- **Linux:** `libllama.so`, `libmtmd.so` (and versioned variants like `libmtmd.so.0`, `libmtmd.so.0.4.0`) in `runtimes/linux-x64/`.
- **CLI Tools:** `llama-cli`, `llama-bench`, `llama-quantize`, `llama-perplexity`, `llama-mtmd-cli`, etc.

**Purpose:** Pre-compiled native binaries for llama.cpp and mtmd (multi-modal) backends. Automatically copied to output directory during build based on target OS.

---

## Multi-Modal Context Handler

**File:** `src/InstantAIGate.Native/Core/MultiModalContext.cs`

**Role:** Manages multi-modal (vision/audio) context for VLM models. Wraps `mtmd` native library for image and audio processing.

**Key Properties:**
- `IsInitialized` — whether context is ready.
- `SupportsVision` — whether model supports image input.
- `SupportsAudio` — whether model supports audio input.
- `AudioSampleRate` — required audio sample rate.

**Invariants:**
- Must be disposed to free native resources.
- Vision support requires projector file (mmproj/clip).

---

## Usage Notes for AI Agents

1. When generating inference code — start with `LlamaInference.cs`, then check `BackendFacade.cs` for native calls.
2. When working with sessions — key file is `SessionInferenceManager.cs`. All KV-cache operations go through it.
3. When adding new CLI commands — create class in `src/InstantAIGate.Cli/Commands/`, implement `IConsoleCommand`, register in `Program.cs`.
4. When modifying SignalR contract — update `IGatewayHubClient.cs` and corresponding handlers in `RemoteGatewayClient.cs` (CLI) and web client.
5. When working with VRAM budgeting — check `BackendFacade.CreateContext` (parameter `KvCacheQuantization`) and `HardwareProfileSettings`.
6. 32B models require `KvCacheQuantization: "Q8_0"` and `MaxContexts: 2` in profile.
7. Native libraries are in `runtimes/` directory — do not modify binaries, only update via official llama.cpp releases.
8. Multi-modal support requires both text model and projector file (mmproj/clip) — check `MultiModalContext.cs` and `NativeModelValidator.cs`.
9. When adding embeddings — ensure `ModelSettings.Embeddings = true` and hardware profile has `Embeddings: true`.
10. `PrefixTreeContextPool.cs` exists as a reserved prototype for TTFT optimization but is NOT registered in DI and NOT used by any active service.