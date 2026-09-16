# Changelog

## [Unreleased] - 2025-09-16

### Summary
**Phase 5 (Native Layer Wiring) is now functionally complete.** The inference mechanism using llama.cpp with Vulkan backend drivers is fully implemented and operational. The CLI application successfully launches and produces test messages from the inference engine. Linux version has not been tested yet.

---

## Phase 5: Native Layer Wiring ✅ COMPLETED

### Added
- **InstantAIGate.Native** module with complete P/Invoke bindings for `llama.h` and `mtmd.h` (llama.cpp v0.4.0)
- **BackendFacade** (`InstantAIGate.Native/Inference/BackendFacade.cs`) - Implementation of `IBackendFacade` mapping Core abstract types to native llama.cpp P/Invoke calls:
  - Backend initialization and cleanup (`LoadAllBackends`, `BackendInit`, `BackendFree`)
  - GPU offload support detection (`SupportsGpuOffload`)
  - Model loading with Vulkan backend configuration (`LoadModel`)
  - Context creation with FlashAttention and KV cache quantization support (`CreateContext`)
  - Memory management (`FreeModel`, `FreeContext`, `GetMemory`, `ClearMemory`)
  - Native logging callback integration (`SetLogCallback`)
  
- **LlamaInference** (`InstantAIGate.Native/Inference/LlamaInference.cs`) - Core inference engine implementing `IInferenceEngine`:
  - Tokenization via `llama_tokenize` with proper buffer handling
  - Streaming generation with sampler chain (top_k, top_p, temperature, distribution)
  - Batch prompt evaluation with configurable batch sizes
  - Hardware-level stop token detection (including Qwen-specific tokens: 151645, 151643)
  - UTF-8 decoding with proper character buffer management
  - Chat template application via `llama_chat_apply_template`
  
- **VisionFacade** (`InstantAIGate.Native/Inference/VisionFacade.cs`) - Implementation of `IVisionFacade` for multimodal support:
  - Vision context initialization via `mtmd_init_from_file`
  - Projector model binding to base text model
  
- **Dependency Injection** (`InstantAIGate.Native/DependencyInjection/ServiceCollectionExtensions.cs`):
  - `AddInstantAIGateInference()` extension method for service registration
  - Singleton registration for stateful native resource management
  
- **CLI Application** (`InstantAIGate.Cli`):
  - Working test harness for inference validation
  - Model loading with Qwen3VL-8B-Instruct configuration
  - Interactive chat message streaming with stop token handling
  - Local model path provider implementation
  
- **ModelProvider enhancements**:
  - Context pooling with `ConcurrentBag<IntPtr>` for efficient reuse
  - Automatic projector/text model detection and correction
  - Graceful model swapping with request draining
  - stderr redirection for native llama.cpp logging
  - Static logger integration for native callback handling
  
- **ModelManager**:
  - Thread-safe model lifecycle management with `SemaphoreSlim`
  - Active lease tracking for graceful shutdown
  - Hot-swap capability with queue pause/resume
  - Metrics exposure (`InferenceMetrics` with lease count and pending requests)

### Changed
- Updated `ARCHITECTURE_CONCEPT.md` to reflect P/Invoke implementation details
- Added 5th architectural principle: "Native Performance via P/Invoke"
- Enhanced project structure documentation with detailed file breakdown in `InstantAIGate.Native/`
- Clarified Vulkan backend positioning in module objectives

### Technical Decisions
- Direct P/Invoke calls eliminate HTTP proxy overhead for inference operations
- SafeHandle wrappers ensure proper native resource lifetime management
- Platform-specific library loading from application root directory
- Consistent error handling with explicit exception throwing on native failures
- Sampler chain intentionally omits penalty samplers for Instruct models to prevent penalty collapse
- Logit index calculation corrected to avoid memory access violations during generation

### Dependencies
- .NET 8+ required (updated from .NET 6+)
- Native llama.cpp binaries with Vulkan backend (provided separately in `/runtimes`)
- Vulkan runtime drivers installed on host system

---

## Phase 4: Multimodal & Advanced Features ⏳ IN PROGRESS

### Added
- **VisionContext** support in `ModelProvider` with automatic projector detection
- **MultiModalContext** wrapper in `InstantAIGate.Native/Core/` for mtmd operations

### Pending
- Vision context pooling (separate pool for multimodal contexts)
- Image preprocessing pipeline optimization for batch processing
- Integration tests for the full multimodal workflow
- Batch inference and streaming optimization with backpressure

---

## Phase 3: Reliability & Observability ⏳ PENDING

### Planned
- Circuit breaker pattern (fallback to CPU on GPU errors)
- Detailed metrics collection (latency percentiles, cache hit/miss, memory usage)
- Graceful shutdown (state preservation and safe teardown)
- Error recovery (automatic reload on native crashes)

---

## Phase 2: Concurrency & Performance ⏳ PENDING

### Planned
- Lock-free context acquisition optimization for the hot path
- Async context pre-warming (parallel initialization of multiple contexts)
- Memory pressure handling (automatic release of idle contexts)
- Thread pool tuning (dynamic CPU thread allocation for large models)
- VRAM monitoring and limits

---

## Phase 1: Core Infrastructure ✅ COMPLETED

### Completed
- Created abstract native types (`BackendEnums.cs`) to decouple Core from Native
- Created `ModelType` enum and updated configuration DTOs
- Defined Core interfaces (`IModelManager`, `IModelProvider`, `IBackendFacade`, `IVisionFacade`, `IModelPathProvider`)
- Implemented `RequestQueue` with backpressure, pause/resume, and thread-safe counters
- Refactored and migrated `ModelProvider` to Core (immutability, interface-driven)
- Refactored and migrated `ModelManager` to Core (graceful draining, interface-driven)

---

## Known Issues
- **Linux Vulkan backend**: Not yet tested. Build infrastructure is in place (`deploy/docker/Dockerfile`, `runtimes/build/start.sh`), but runtime validation pending.
- **SSR Module**: Downloader, Registry, and Telemetry submodules are stubbed (`.gitkeep` placeholders). Model loading currently relies on manual file placement.
- **Server Module**: ASP.NET Core hosting layer is minimal skeleton. OpenAI-compatible controllers and SignalR hubs not yet implemented.

---

## File Statistics
- **Total C# files**: 37
- **Total lines of code**: ~5,395
- **Modules**: 5 (Core, Native, SSR, Server, Cli)
- **Native bindings**: 110+ DllImport functions (70+ llama.h, 40+ mtmd.h)

