# AI File Guide — InstantAIGate Architecture Map

**Document Version:** 1.5 (Multi-Model Runtime)  
**Last Updated:** Reflects codebase state (`v1.5.0-rc.2`) with concurrent multi-model VRAM pool management, automatic embedding model routing (`EmbeddingModelResolver`), hardware profile policy (`HardwareProfilePolicy`), multi-model startup loading, connection-bound session lifecycle with disconnect Grace Period and idle eviction, gateway state machine, and VRAM purging.

This file describes the key modules of the repository. Use it as a navigation map when analyzing or generating code.

---

## Server Entry Point
**File:** `src/InstantAIGate.Server/Program.cs`  
**Role:** ASP.NET Core application entry point. Registers the DI container, middleware, SignalR hubs, hosted services, and health checks.

**Key Dependencies:**
* `InstantAIGate.Native.DependencyInjection.ServiceCollectionExtensions` — registers the native inference stack (`BackendFacade`, `LlamaInference`, `ModelProvider`, `ModelManager`, `EmbeddingModelResolver`, `SessionInferenceManager`).
* `InstantAIGate.SSR.DependencyInjection.ServiceCollectionExtensions` — registers the model downloader and catalog service.
* `InstantAIGate.Server.Configuration.HardwareProfilePolicy` — resolves hardware profiles and auto-routes embedding models to the `Embedding` profile.
* `InstantAIGate.Server.Configuration.StartupModelSettings` — configures single or multi-model autoload on boot.
* `InstantAIGate.Server.Hubs.GatewayHub` — central SignalR hub.
* `InstantAIGate.Server.Services.Workers.*` — background tasks (`ModelStartupWorker`, `ModelDownloadWorker`, `MetricsBroadcasterWorker`).
* `InstantAIGate.Core.Dtos.Config.SessionSettings` — session disconnect grace period and idle timeout options.

**Invariants:**
* Health check `/health/live` returns `200 OK` without authorization (for orchestrators).
* Health check `/health/ready` returns `503 Service Unavailable` until at least one model is loaded and status is `Ready`, exposing both `activeModelId` and `activeModels` in payload data.
* SignalR hub is mapped to `/hub/gateway`.
* `ApiKeyAuthMiddleware` authenticates all non-health requests.
* `GatewayExceptionMiddleware` catches `QueueFullException` (`429`), `TimeoutException` (`504`), model draining/inactive `InvalidOperationException` (`503`), and unhandled exceptions (`500`).

---

## Central SignalR Hub
**File:** `src/InstantAIGate.Server/Hubs/GatewayHub.cs`  
**Role:** Single entry point for the data plane (stateful chat inference, stateless embeddings, model discovery) and control plane (multi-model lifecycle management, downloads, queue limits).

**Key Dependencies:**
* `ISessionInferenceManager` — session state, KV-cache, disconnect grace period, and idle eviction.
* `IInferenceEngine` — token streaming and embedding vector generation.
* `IModelManager` — multi-model VRAM pool loading, type-scoped swapping, targeted unloading, and idle context purging.
* `IEmbeddingModelResolver` — automatic resolution of embedding-capable models in the active VRAM pool.
* `HardwareProfilePolicy` — automatic hardware profile selection (`Default` vs. `Embedding`).
* `IQueueManager` — concurrency control.
* `IGatewayStateManager` — gateway operational status tracking.
* `ChannelWriter<DownloadJob>` — background download queue.

**Invariants:**
* All public methods require `[Authorize]`.
* Data plane methods (`JoinSession`, `LeaveSession`, `DestroySession`, `SendPromptDelta`, `RollbackSession`, `ShiftSessionCache`, `GetSessionTokenCount`, `GetGatewayStatus`, `GetActiveModelDetailsAsync`, `GetLoadedModelsDetailsAsync`, `GenerateEmbeddingsAsync`, `GetEmbeddingDimensionAsync`, `SubscribeToModelDownload`, `UnsubscribeFromModelDownload`) require the `GatewayUser` policy.
* Control plane methods (`LoadModelAsync`, `SwapModelAsync`, `UnloadModelAsync`, `DownloadModelAsync`, `PurgeIdleContextsAsync`, `SetQueueLimitAsync`, `GetModelsAsync`, `GetQueueMetricsAsync`) require the `GatewayAdmin` policy.
* On disconnect, a configurable Grace Period (default `30s`) is armed for all sessions bound to that `ConnectionId`; reconnecting within this window preserves the KV-cache without full prompt re-decoding.
* `JoinSession(sessionId, repoId)` resolves the primary active model if `repoId` is empty, or validates that the requested `repoId` is currently loaded in `ModelManager`.
* `GenerateEmbeddingsAsync` and `GetEmbeddingDimensionAsync` resolve their target model through `IEmbeddingModelResolver.Resolve(repoId)`, allowing clients to pass `""` or even a chat model ID when a dedicated embedding model is loaded in VRAM.

### Hub Methods (Data Plane — `GatewayUser`)
| Method | Description |
| :--- | :--- |
| `JoinSession(sessionId, repoId)` | Creates or resumes a session bound to `repoId` (or primary chat model if empty) and binds to `Context.ConnectionId` |
| `LeaveSession(sessionId)` | Soft-releases session, returns context to the model's VRAM pool, notifies client |
| `DestroySession(sessionId)` | Hard-destroys session context slot in VRAM (`SuppressPool = true`), notifies client |
| `SendPromptDelta(sessionId, deltaMessage)` | Streams generated tokens via `ReceiveTokenDelta` |
| `RollbackSession(sessionId, targetPosition)` | Truncates session KV-cache to `targetPosition` |
| `ShiftSessionCache(sessionId, startPos, count)` | Applies sliding-window eviction to session KV-cache |
| `GetSessionTokenCount(sessionId)` | Returns current `PastTokens` count for the session |
| `GetGatewayStatus()` | Returns current `GatewayStatusDetails` snapshot (including `ActiveModelIds`) |
| `GetActiveModelDetailsAsync()` | Returns `NativeModelDetails` for the primary active model |
| `GetLoadedModelsDetailsAsync()` | Returns `IEnumerable<NativeModelDetails>` for all models currently loaded in VRAM |
| `SubscribeToModelDownload(repoId)` | Joins the `download_{repoId}` progress broadcast group |
| `UnsubscribeFromModelDownload(repoId)` | Leaves the `download_{repoId}` progress broadcast group |
| `GenerateEmbeddingsAsync(repoId, request)` | Generates vector embeddings using `IEmbeddingModelResolver` |
| `GetEmbeddingDimensionAsync(repoId)` | Returns embedding vector dimension (`n_embd`) using `IEmbeddingModelResolver` |

### Hub Methods (Control Plane — `GatewayAdmin`)
| Method | Description |
| :--- | :--- |
| `LoadModelAsync(repoId, profileName)` | Loads a model into the VRAM pool alongside existing models using `HardwareProfilePolicy` |
| `SwapModelAsync(repoId, profile)` | Type-scoped hot-swap: unloads only active models of the same `ModelType`, then loads the new model |
| `UnloadModelAsync(repoId)` | Drains and unloads a specific model from VRAM; resets state only if no models remain |
| `DownloadModelAsync(repoId)` | Enqueues a background model download job |
| `PurgeIdleContextsAsync(repoId)` | Frees pooled idle contexts for `repoId` (or across all models if `repoId` is empty) |
| `SetQueueLimitAsync(limit)` | Updates dynamic request queue capacity |
| `GetModelsAsync()` | Lists `IEnumerable<ModelRegistryStatus>` for all loaded models |
| `GetQueueMetricsAsync()` | Returns `InferenceMetrics` (`ActiveLeases`, `PendingRequests`) |

---

## Hub Client Contract
**File:** `src/InstantAIGate.Server/Hubs/IGatewayHubClient.cs`  
**Role:** Strongly-typed interface describing all server-to-client push callbacks.

| Method | Description |
| :--- | :--- |
| `ReceiveTokenDelta(SessionTokenDelta)` | Generated text chunk or completion marker (`IsDone = true`) |
| `ReceiveContextOverflow(ContextOverflowException)` | Context window limit exceeded |
| `ReceiveError(string)` | Operational error message |
| `ReceiveSessionClosed(string)` | Confirmation that a session was closed |
| `ReceiveDownloadProgress(DownloadProgress)` | Real-time model download progress |
| `ReceiveQueuePosition(int)` | Inference queue position |
| `ReceiveLog(string, string, string)` | Server diagnostic logs (admin group only) |
| `ReceiveMetrics(InferenceMetrics, object)` | Active lease metrics and native model details (admin group only) |
| `ReceiveGatewayStatus(GatewayStatusDetails)` | Gateway operational status snapshot |

---

## Gateway State Manager
**File:** `src/InstantAIGate.Core/Services/Inference/GatewayStateManager.cs`  
**Role:** Stores current gateway operational status, tracks the set of all ready models in memory (`_readyModels`), and publishes changes via the `StatusChanged` event.

**Key Concepts:**
* Thread-safe via `lock (_syncLock)`.
* Tracks `_readyModels` (`HashSet<string>`) so `GatewayStatusDetails.ActiveModelIds` always reflects all ready models in the VRAM pool while `ActiveModelId` reflects the primary/latest target model.
* `SetReady(modelId)` adds `modelId` to `_readyModels`.
* `SetFaulted(modelId, ...)` removes `modelId` from `_readyModels` and formats the exception message.
* `Reset()` clears `_readyModels` and transitions back to `Uninitialized`.

---

## Session State Manager
**File:** `src/InstantAIGate.Core/Services/Inference/SessionInferenceManager.cs`  
**Role:** Single source of truth for stateful session lifecycle management: per-session model binding (`RepoId`), token counters (`PastTokens`), prefix token sequences, native `InferenceContext` (KV-cache), SignalR connection binding, disconnect Grace Period, and idle timeout eviction.

**Key Concepts:**
* `StatefulSessionEntry` — holds `Request` (including `RepoId`), `PastTokens`, `TokenSequence`, `ExecutionGate`, `ContextInitGate`, `BoundConnectionId`, and `DisconnectGraceCts`.
* `GetOrCreateContextAsync(sessionId)` — lazily acquires an `InferenceContext` from `IModelManager.AcquireContextAsync(session.RepoId)` so each session draws from its bound model's context pool.
* `AcquireSessionExecutionGateAsync` — serializes concurrent generation/memory operations within the same session.
* `ReleaseSessionAsync` — awaits `ExecutionGate` with `CancellationToken.None` before disposing the native context, preventing native Access Violation (`0xc0000005`) crashes during active `llama_decode` calls.
* `NotifyConnectionDisconnected(connectionId)` — starts a delayed eviction timer (`DisconnectGracePeriod`, default `30s`) for all sessions bound to the dropped connection.
* `CreateSessionAsync(request, connectionId)` — idempotent; if `sessionId` exists, cancels `DisconnectGraceCts`, rebinds `BoundConnectionId`, and preserves the hot KV-cache.

---

## Multi-Model Lifecycle Manager & Provider
**Files:**
* `src/InstantAIGate.Core/Services/Inference/ModelManager.cs`
* `src/InstantAIGate.Core/Services/Inference/ModelProvider.cs`

**Role:** Manages the multi-model VRAM registry, per-model context pools, per-model concurrency semaphores, type-scoped hot-swapping, and safe draining.

**Key Concepts (`ModelManager`):**
* `_activeModels` (`ConcurrentDictionary<string, ModelSettings>`) — registry of all currently loaded models.
* `_contextSemaphores` (`ConcurrentDictionary<string, SemaphoreSlim>`) — enforces `MaxContexts` independently per `repoId`.
* `_modelLeases` & `_drainingModels` — tracks active context leases per model. Unloading a model sets `_drainingModels[repoId] = true` and waits only for that model's leases (`_modelLeases[repoId] == 0`) to finish.
* `_primaryRepoId` — tracks the primary generative model (automatically updated when a non-embedding model is loaded).
* `GetActiveSettings(string? repoId = null)` — returns settings for `repoId` if provided; otherwise returns settings for `_primaryRepoId` (or the first non-embedding model).
* `SwapModelAsync(ModelSettings newConfig)` — unloads only existing active models where `m.Type == newConfig.Type`, preserving models of other types in VRAM.
* `PurgeIdleContextsAsync(string repoId)` — purges pooled idle contexts for `repoId`, or across all active models if `repoId` is empty/whitespace.

**Key Concepts (`ModelProvider`):**
* Maintains `_modelCache` (`IModelHandle`), `_visionCache` (`VisionContext`), `_configCache` (`ModelSettings`), `_pools` (`ConcurrentBag<IContextHandle>`), and `_initLocks` keyed by `repoId`.
* `GetNativeDetails()` — enumerates all loaded models in `_modelCache` and populates `NativeModelDetails` (including `EmbeddingDimension` and `IsEmbedding`).

---

## Embedding Model Resolver
**File:** `src/InstantAIGate.Core/Services/Inference/EmbeddingModelResolver.cs`  
**Role:** Resolves the target `repoId` for embedding generation and dimension queries in a multi-model pool.

**Resolution Order (`Resolve(string? requestedRepoId)`):**
1. If `requestedRepoId` is non-empty and matches an active model with `Embeddings: true`, returns `requestedRepoId`.
2. If a dedicated embedding model (`m.Embeddings || m.Type == ModelType.Embedding`) is loaded in `IModelManager.ActiveModels`, automatically routes to it (logging an informational message if `requestedRepoId` was a non-embedding chat model).
3. Otherwise throws an informative `InvalidOperationException`.

---

## Hardware Profile Policy
**File:** `src/InstantAIGate.Server/Configuration/HardwareProfilePolicy.cs`  
**Role:** Centralizes hardware profile resolution and prevents misconfiguration when loading embedding models.

**Invariants:**
* `IsEmbeddingModel(repoId, architecture)` returns `true` if `repoId` or `architecture` contains `"embed"` (case-insensitive).
* `ResolveProfileName(requestedProfile, isEmbeddingModel)` keeps `requestedProfile` for chat/VLM models, but automatically reroutes embedding models from non-embedding profiles (such as `"Default"`) to `"Embedding"` if the `"Embedding"` profile exists in configuration.

---

## Request Concurrency Control
**File:** `src/InstantAIGate.Core/Services/Inference/DynamicRequestQueue.cs`  
**Role:** Limits concurrent gateway requests via `SemaphoreSlim` and supports dynamic queue capacity adjustments (`UpdateQueueLimit`).

---

## Native Inference Engine & Backend Facade
**Files:**
* `src/InstantAIGate.Native/Inference/LlamaInference.cs`
* `src/InstantAIGate.Native/Inference/BackendFacade.cs`
* `src/InstantAIGate.Native/Inference/VisionFacade.cs`
* `src/InstantAIGate.Native/Inference/LlamaModelLocator.cs`
* `src/InstantAIGate.Native/Inference/NativeModelValidator.cs`

**Role:** P/Invoke bridge and execution engine over `llama.cpp` (`llama`) and `mtmd`.

**Invariants (`LlamaInference`):**
* `StreamDeltaGenerationAsync` looks up the session's bound `repoId` via `_sessionManager.TryGetSessionRepoId(sessionId, out var repoId)` and acquires weights/settings for that specific model.
* Throws `ContextOverflowException` before decoding if `pastTokens + estimatedTokens + requiredReserve > maxContextLimit`.
* `GenerateEmbeddingsAsync(modelId, request)` verifies `activeSettings.Embeddings == true`, acquires a temporary context from `modelId`'s pool, decodes tokens in batches, extracts embeddings via `_backendFacade.GetEmbeddings`, optionally applies $L_2$ normalization, and returns the context to the pool on dispose.

---

## Background Workers

### `ModelStartupWorker`
**File:** `src/InstantAIGate.Server/Services/Workers/ModelStartupWorker.cs`  
**Role:** Automatically downloads (if missing) and loads configured startup model(s) on server boot.
* Supports multi-model startup configuration via `StartupModelSettings.Models` (`List<StartupModelItem>`), `StartupModelSettings.RepoId` (`string[]`), or scalar `InstantAIGate:StartupModel:RepoId`.
* Applies `HardwareProfilePolicy` per model entry and transitions `IGatewayStateManager` to `Ready` with the primary chat model once loading completes.

### `ModelDownloadWorker`
**File:** `src/InstantAIGate.Server/Services/Workers/ModelDownloadWorker.cs`  
**Role:** Processes background `DownloadJob` items from `ChannelReader<DownloadJob>` and broadcasts `ReceiveDownloadProgress` to `download_{modelId}` and `GatewayAdminGroup`.

### `MetricsBroadcasterWorker`
**File:** `src/InstantAIGate.Server/Diagnostics/MetricsBroadcasterWorker.cs`  
**Role:** Event-driven broadcaster that listens to `IMetricsEventSource.Reader` and `IGatewayStateManager.StatusChanged`, pushing updates to SignalR clients without polling.

---

## Parallel Model Downloader
**File:** `src/InstantAIGate.SSR/Downloader/ParallelModelDownloader.cs`  
**Role:** Downloads GGUF model files with HTTP Range support, 4-chunk parallel streaming, `.meta.json` resume manifests, and native `IModelValidator` header verification.

---

## CLI Entry Point, Session State & Commands
**Files:**
* `src/InstantAIGate.Cli/Program.cs`
* `src/InstantAIGate.Cli/CliHostedService.cs`
* `src/InstantAIGate.Cli/State/CliSession.cs`
* `src/InstantAIGate.Cli/Core/IGatewayClient.cs` (`LocalGatewayClient.cs`, `RemoteGatewayClient.cs`, `GatewayClientProxy.cs`)

**Multi-Model CLI State (`CliSession`):**
* `ActiveModelId` — target `repoId` for interactive chat prompts.
* `ActiveEmbeddingModelId` — target `repoId` for `/embed` and `/embed-dim` commands (auto-synced from loaded models on `/connect` or `/load`).
* `LoadedModelIds` — set of models loaded during the CLI session.

### CLI Commands Reference (`src/InstantAIGate.Cli/Commands/`)
| Command | File | Role |
| :--- | :--- | :--- |
| `/load <modelId> [profile]` | `LoadCommand.cs` | Loads a model into the VRAM pool (defaults to `Embedding` profile for embed models, sets `ActiveModelId` or `ActiveEmbeddingModelId`) |
| `/use [modelId]` | `UseCommand.cs` | Displays or switches the active chat model (`ActiveModelId`) among models currently loaded in VRAM and starts a fresh session |
| `/unload <modelId>` | `UnloadCommand.cs` | Unloads a specific model from VRAM and falls back to remaining loaded models |
| `/vram [purge [modelId]]` | `VramCommand.cs` | Displays a table of all loaded models in VRAM (via `GetLoadedModelsDetailsAsync`) or purges idle contexts for one/all models |
| `/embed <text>` | `EmbedCommand.cs` | Generates normalized embeddings using `ActiveEmbeddingModelId` (or gateway auto-resolution) |
| `/embed-dim [modelId]` | `EmbedDimCommand.cs` | Queries the embedding dimension (`n_embd`) of the specified or active embedding model |
| `/ctx info\|shift\|rollback\|clear` | `ContextCommand.cs` | Inspects or mutates session KV-cache state |
| `/image <path>` | `ImageCommand.cs` | Queues a local image file for the next multimodal prompt |
| `/connect [url] [key] \| local` | `ConnectCommand.cs` | Switches between local engine and remote SignalR gateway, syncing both `ActiveModelId` and `ActiveEmbeddingModelId` |
| `/download <modelId>` | `DownloadCommand.cs` | Downloads a model from the catalog locally or on the remote server |
| `/models` | `ModelsCommand.cs` | Lists all supported models from `config/model_catalog.json` |
| `/debug [on\|off]` | `DebugCommand.cs` | Toggles native and remote server diagnostic logs |
| `/help` | `HelpCommand.cs` | Displays available CLI commands |

---

## Usage Notes for AI Agents
* **Multi-Model Invariant:** Never assume only one model is loaded in `ModelManager` or `ModelProvider`. Always resolve settings and contexts by `repoId` (`GetModelSettings(repoId)` or `GetActiveSettings(repoId)`).
* **Embedding Routing:** Always route external embedding requests through `IEmbeddingModelResolver.Resolve(repoId)` before calling `IInferenceEngine.GenerateEmbeddingsAsync` or `IModelManager.GetEmbeddingDimensionAsync`.
* **Hardware Profiles:** When loading models on the server, use `HardwareProfilePolicy` so embedding models automatically receive `Embeddings: true`.
* **SignalR Contract Updates:** When modifying hub methods, keep `IGatewayHub.cs`, `IGatewayHubClient.cs`, `GatewayHub.cs`, `IGatewayClient.cs`, `LocalGatewayClient.cs`, `RemoteGatewayClient.cs`, and `GatewayClientProxy.cs` synchronized.
* **Reserved Prototype:** `PrefixTreeContextPool.cs` is a standalone prototype for prefix caching and is not registered in DI.