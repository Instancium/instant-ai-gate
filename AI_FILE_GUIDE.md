AI File Guide — InstantAIGate Architecture Map
Document Version: 1.1

This file describes the key modules of the repository. Use it as a navigation map when analyzing or generating code.

1. Server Entry Point
File: `src/InstantAIGate.Server/Program.cs`
Role: ASP.NET Core application entry point. Registers DI container, middleware, SignalR hubs, hosted services, and health checks.
Key Dependencies:
- `InstantAIGate.Native.DependencyInjection.ServiceCollectionExtensions` — registers native stack (BackendFacade, LlamaInference, ModelManager).
- `InstantAIGate.SSR.DependencyInjection.ServiceCollectionExtensions` — registers model downloader and catalog.
- `InstantAIGate.Server.Hubs.GatewayHub` — central SignalR hub.
- `InstantAIGate.Server.Services.Workers.*` — background tasks (ModelStartupWorker, ModelDownloadWorker, MetricsBroadcasterWorker).
Invariants:
- Health check `/health/live` returns 200 without authorization (for orchestrators).
- Health check `/health/ready` returns 503 until model is loaded.
- SignalR hub is mapped to `/hub/gateway`.
Scenarios:
- Running server as Windows service (`UseWindowsService`).
- Intercepting native llama.cpp logs via `NativeStreamRedirector`.

2. Central SignalR Hub
File: `src/InstantAIGate.Server/Hubs/GatewayHub.cs`
Role: Single entry point for data plane (inference) and control plane (model management). Handles authorization, queues, sessions, and token streaming.
Key Dependencies:
- `ISessionInferenceManager` — session state and KV-cache management.
- `IInferenceEngine` — token generation delegation.
- `IModelManager` — model loading/unloading.
- `IQueueManager` — concurrency control.
- `IEphemeralSessionRegistry` — session registration with auto-deletion on disconnect.
Invariants:
- All public methods require `[Authorize]`.
- Data plane methods (`SendPromptDelta`, `JoinSession`) require `GatewayUser` role.
- Control plane methods (`LoadModelAsync`, `SwapModelAsync`) require `GatewayAdmin` role.
- On disconnect, all registered sessions are released; ephemeral sessions are deleted with `destroySlot: true`.
Scenarios:
- Token streaming via `SendPromptDelta` → `ReceiveTokenDelta`.
- Handling `ContextOverflowException` via `ReceiveContextOverflow`.
- Subscribing to model download progress via SignalR groups.

3. Hub Client Contract
File: `src/InstantAIGate.Server/Hubs/IGatewayHubClient.cs`
Role: Interface describing all methods the server calls on the client (push notifications).
Methods:
- `ReceiveTokenDelta` — generated text chunk.
- `ReceiveContextOverflow` — context window exceeded.
- `ReceiveError` — general error.
- `ReceiveSessionClosed` — session closed.
- `ReceiveDownloadProgress` — model download progress.
- `ReceiveQueuePosition` — inference queue position (Reserved/Not emitted in current build).
- `ReceiveLog` — server logs (admin only).
- `ReceiveMetrics` — inference metrics (admin only).
- `ReceiveGatewayStatus` — gateway status.

4. Session State Manager
File: `src/InstantAIGate.Core/Services/Inference/SessionInferenceManager.cs`
Role: Stores state of all active sessions: token counter, token sequence, reference to `InferenceContext` (KV-cache). Ensures thread-safe access via `SemaphoreSlim` gates.
Key Concepts:
- `StatefulSessionEntry` — internal session record with `PastTokens`, `TokenSequence`, `ExecutionGate`, `ContextInitGate`.
- `AcquireSessionExecutionGateAsync` — blocks concurrent generation in the same session.
- `RollbackToPositionAsync` — truncates KV-cache to specified position via `IBackendFacade.RemoveContextMemoryRange`.
- `ShiftMemoryRangeAsync` — shifts KV-cache (sliding window) if `CanShiftContextMemory == true`.
Invariants:
- `RollbackToPositionAsync` throws `ArgumentOutOfRangeException` if `targetPosition > PastTokens`.
- `ShiftMemoryRangeAsync` throws `NotSupportedException` if FlashAttention or GPU-offload prohibits shifting.
- Idle sessions are automatically released by timer (default 10 minutes).
Scenarios:
- CLI command `/ctx rollback <pos>`.
- CLI command `/ctx shift <start> <count>`.
- Handling `ContextOverflowException` in `LlamaInference`.

5. Model Lifecycle Manager
File: `src/InstantAIGate.Core/Services/Inference/ModelManager.cs`
Role: Manages model lifecycle: loading, hot-swap, unloading. Coordinates `IModelProvider` and `IQueueManager`.
Key Concepts:
- `_globalLock` (SemaphoreSlim) — prevents parallel load/unload operations.
- `_activeLeases` (Interlocked) — counter of active inference operations.
- Graceful swap: pause queue → wait for active leases to complete → unload old model → load new model → resume queue.
Invariants:
- `AcquireContextAsync` throws `InvalidOperationException` if model is not active or draining.
- `SwapModelAsync` waits until `_activeLeases == 0` before unloading old model.
Scenarios:
- Admin command `LoadModelAsync` in `GatewayHub`.
- Admin command `SwapModelAsync` in `GatewayHub`.
- Automatic startup model loading via `ModelStartupWorker`.

6. Request Concurrency Control
File: `src/InstantAIGate.Core/Services/Inference/DynamicRequestQueue.cs`
Role: Limits concurrent inference requests. Implements backpressure via `SemaphoreSlim` and dynamic limit adjustment.
Key Concepts:
- `EnqueueRequestAsync` — atomically increments `_pendingCount`, waits for semaphore slot, returns `IQueueLease`.
- `UpdateQueueLimit` — dynamically changes `_maxQueueSize` (release/acquire slots).
- `Pause/Resume` — flag `_isPaused` blocks new slot issuance (used during model hot-swap).
Invariants:
- `QueueFullException` thrown if `PendingCount >= MaxQueueSize`.
- `TimeoutException` thrown if slot not acquired within `timeout`.
- On token cancellation, `_pendingCount` is decremented.
Scenarios:
- Gateway-level rate limiting.
- Admin command `SetQueueLimitAsync`.
- Queue pause during `SwapModelAsync`.

7. KV-Cache Context Pool (Internal / Reserved)
File: `src/InstantAIGate.Core/Services/Inference/PrefixTreeContextPool.cs` & `ModelProvider.cs`
Role: The `ModelProvider` manages warm context pools per model using a simple `ConcurrentBag<IContextHandle>`. Released contexts are cleared and returned for reuse.
> ⚠️ **Note:** `PrefixTreeContextPool.cs` exists in the codebase as a reserved prototype for TTFT optimization (prefix matching). It is **NOT** registered in DI and **NOT** used by any active service in the current build.

8. Native Inference Engine
File: `src/InstantAIGate.Native/Inference/LlamaInference.cs`
Role: Primary inference engine. Converts `ChatMessage` → tokens → llama_decode → token streaming. Works directly with native handles via unsafe code.
Key Dependencies:
- `IBackendFacade` — abstraction over P/Invoke.
- `ISessionInferenceManager` — session state management.
- `IModelManager` — obtaining `ModelWeights` and `InferenceContext`.
- `IMediaResolver` — image processing for VLM.
Invariants:
- Throws `ContextOverflowException` if `PastTokens + EstimatedTokens + Reserve > ContextSize`.
- Removes BOS token from delta if `PastTokens > 0`.
- Uses unsafe code for direct `llama_batch` population.
- UTF-8 decoding via `Decoder.GetChars` with flush at end.
Scenarios:
- Token streaming via `StreamDeltaGenerationAsync`.
- Processing multimodal requests (images via `mtmd_tokenize`).
- Tokenization and chat-template application.

9. Native Backend Facade
File: `src/InstantAIGate.Native/Inference/BackendFacade.cs`
Role: Facade over P/Invoke bindings for `llama.cpp`. Converts managed objects (`ModelSettings`) to native parameters and wraps native pointers in `IModelHandle`/`IContextHandle`.
Key Methods:
- `LoadModel` → `llama_model_load_from_file`.
- `CreateContext` → `llama_init_from_model` with `TypeK`/`TypeV` configuration (F16/Q8_0/Q4_K).
- `RemoveContextMemoryRange` → `llama_memory_seq_rm`.
- `ShiftContextMemoryRange` → `llama_memory_seq_add`.
- `CanShiftContextMemory` → `llama_memory_can_shift` (false with FlashAttention).
Invariants:
- `KvCacheQuantization` maps to `GgmlType` (F16=2 bytes, Q8_0=1 byte, Q4_K=0.5 byte).
- `FlashAttention` disables KV-cache shifting capability.
Scenarios:
- Creating context with specified size and quantization.
- Sliding window operations on KV-cache.

10. CLI Entry Point
File: `src/InstantAIGate.Cli/Program.cs`
Role: Interactive CLI client entry point. Configures DI, registers commands, creates `GatewayClientProxy` (local or remote mode).
Key Dependencies:
- `CommandDispatcher` — routes commands by `/` prefix.
- `GatewayClientProxy` — proxy between `LocalGatewayClient` and `RemoteGatewayClient` for seamless local/remote switching.
- `CliHostedService` — main REPL loop.
- `ClientSessionMemoryCoordinator` — orchestrates context operations (shift/rollback/evict) for the CLI.
Invariants:
- Operating mode determined by `--remote [baseUrl] [--key apiKey]` flag.
- Local mode uses direct DI container with native stack.
- Remote mode connects via SignalR.
Scenarios:
- Running CLI in local mode (direct inference).
- Running CLI in remote mode (connecting to server via SignalR).

11. CLI Command Dispatcher
File: `src/InstantAIGate.Cli/Commands/CommandDispatcher.cs`
Role: Routes user input to corresponding command by `/` prefix.
Key Concepts:
- All commands implement `IConsoleCommand` (Name, Description, ExecuteAsync).
- Commands registered via DI as `IConsoleCommand`.
- Input not starting with `/` is treated as chat message and passed to `HandleChatInferenceAsync`.
Scenarios (Active Commands):
- `/load <modelId>` — model loading.
- `/ctx info|shift|rollback|clear` — context management.
- `/image <path>` — attaching image.
- `/connect [url] [key]` or `/connect local` — switch between remote and local gateway.
- `/vram [purge]` — view backend config or purge idle VRAM pool.
- `/download <modelId>` — download model from catalog.
- `/models` — list available models from catalog.
- `/debug on|off` — toggle native runtime and server diagnostic logs.
- `/help` — display structured help and available commands.

12. Parallel Model Downloader
File: `src/InstantAIGate.SSR/Downloader/ParallelModelDownloader.cs`
Role: Downloads GGUF model files with range request support, resume capability, parallel download (4 threads), and integrity validation.
Key Concepts:
- `DownloadManifest` — JSON file `.meta.json` for storing chunk progress.
- `DownloadParallelAsync` — splits file into 4 chunks, downloads in parallel via `Range` header.
- `DownloadSequentialAsync` — fallback for servers without range support.
- After download, calls `IModelValidator.ValidateIntegrityAsync` (native GGUF header check).
Invariants:
- Minimum size for parallel download: 10 MB.
- On cancellation, progress saved in manifest for resume.
- On validation failure, temporary files deleted.
Scenarios:
- Startup model loading via `ModelStartupWorker`.
- Model download on admin request via `GatewayHub.DownloadModelAsync`.

13. Ephemeral Session Registry
File: `src/InstantAIGate.Core/Services/Inference/Session/EphemeralSessionRegistry.cs`
Role: Tracks sessions marked as ephemeral and associates them with `ConnectionId`. On disconnect, returns session list for forced deletion.
Key Concepts:
- `_connectionToSessions` — mapping ConnectionId → HashSet<SessionId>.
- `_sessionRegistrationTime` — registration timestamp for orphan detection.
- `DrainOrphanedSessions` — finds sessions older than threshold (2 minutes) without active connection.
Invariants:
- `NotifyConnectionDisconnected` deletes all connection sessions and returns their list.
- `UnregisterSession` removes session from all connections.
Scenarios:
- CLI sessions are ephemeral by default (auto-cleanup on terminal close).
- `SessionReaperService` periodically calls `DrainOrphanedSessions` to free VRAM.

14. Gateway State Manager
File: `src/InstantAIGate.Core/Services/Inference/GatewayStateManager.cs`
Role: Stores current gateway operational status (Uninitialized/ModelDownloading/ModelLoading/Ready/Faulted) and publishes changes via `StatusChanged` event.
Key Concepts:
- Thread-safe via `lock (_syncLock)`.
- `UpdateAndPublish` — atomically updates state and invokes all subscribers.
- Exceptions in subscribers are ignored (do not block others).
Invariants:
- `SetFaulted` concatenates `errorMessage` and `exception.Message`.
- `Reset` returns status to `Uninitialized`.
Scenarios:
- `ModelStartupWorker` updates status during startup model loading.
- `MetricsBroadcasterWorker` broadcasts status to admin SignalR group.
- Health check `/health/ready` reads snapshot to determine degraded/unhealthy.

15. Native Library Loader
File: `src/InstantAIGate.Native/Bindings/NativeLibraryLoader.cs`
Role: Loads native libraries `llama.dll`/`libllama.so` and `mtmd.dll`/`libmtmd.so` via `NativeLibrary.SetDllImportResolver`.
Key Concepts:
- Searches for libraries in `AppDomain.CurrentDomain.BaseDirectory` or custom directory.
- Calls `llama_backend_init()` after successful loading.
- `Unload()` releases libraries via `NativeLibrary.Free`.
Invariants:
- Loading is idempotent (protected by `LockObj`).
- `IsLoaded` — public flag for native stack readiness.
Scenarios:
- Called in `BackendFacade.LoadAllBackends()`.
- Called in tests before integration scenarios.

16. Native Library Binaries
Directory: `runtimes/`
Contents:
- Windows: `llama.dll`, `mtmd.dll` (and dependencies) in `runtimes/win-x64/`.
- Linux: `libllama.so`, `libmtmd.so` (and versioned variants like `libmtmd.so.0`, `libmtmd.so.0.4.0`) in `runtimes/linux-x64/`.
- CLI Tools: `llama-cli`, `llama-bench`, `llama-quantize`, `llama-perplexity`, `llama-mtmd-cli`, etc.
Purpose: Pre-compiled native binaries for llama.cpp and mtmd (multi-modal) backends. Automatically copied to output directory during build based on target OS.

17. Multi-Modal Context Handler
File: `src/InstantAIGate.Native/Core/MultiModalContext.cs`
Role: Manages multi-modal (vision/audio) context for VLM models. Wraps `mtmd` native library for image and audio processing.
Key Properties:
- `IsInitialized` — whether context is ready.
- `SupportsVision` — whether model supports image input.
- `SupportsAudio` — whether model supports audio input.
- `AudioSampleRate` — required audio sample rate.
Invariants:
- Must be disposed to free native resources.
- Vision support requires projector file (mmproj/clip).
Scenarios:
- Processing images in `LlamaInference` for VLM models.
- Audio transcription for multimodal models.

18. Model Validator
File: `src/InstantAIGate.Native/Inference/NativeModelValidator.cs`
Role: Validates GGUF model file integrity using native llama.cpp library.
Key Concepts:
- Loads model with `VocabOnly = true` and `NoAlloc = true` to read headers without allocating tensors.
- Checks if model handle is successfully created.
- Logs validation result.
Invariants:
- Requires `NativeLibraryLoader.IsLoaded` before validation.
- Deletes temporary files on validation failure.
Scenarios:
- Post-download validation in `ParallelModelDownloader`.
- Pre-load validation in `ModelManager`.

Usage Notes for AI Agents
- When generating inference code — start with `LlamaInference.cs`, then check `BackendFacade.cs` for native calls.
- When working with sessions — key file is `SessionInferenceManager.cs`. All KV-cache operations go through it.
- When adding new CLI commands — create class in `src/InstantAIGate.Cli/Commands/`, implement `IConsoleCommand`, register in `Program.cs`.
- When modifying SignalR contract — update `IGatewayHubClient.cs` and corresponding handlers in `RemoteGatewayClient.cs` (CLI) and web client.
- When working with VRAM budgeting — check `BackendFacade.CreateContext` (parameter `KvCacheQuantization`) and `HardwareProfileSettings`.
- 32B models require `KvCacheQuantization: "Q8_0"` and `MaxContexts: 2` in profile — see VRAM Budgeting section in `AI_SYSTEM_PROMPT.md`.
- Native libraries are in `runtimes/` directory — do not modify binaries, only update via official llama.cpp releases.
- Multi-modal support requires both text model and projector file (mmproj/clip) — check `MultiModalContext.cs` and `NativeModelValidator.cs`.