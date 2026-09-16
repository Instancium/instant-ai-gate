# InstantAIGate Architecture Document

**Version:** 1.0  
**Date:** 2025-09-16  
**Status:** Phase 5 Complete - Native Inference Operational

---

## Executive Summary

InstantAIGate is a **foundational infrastructure component** of the Instancium R&D lab ecosystem, providing architectural autonomy and independence by default for local AI inference. The module delivers high-performance, cross-platform inference capabilities through direct P/Invoke integration with `llama.cpp` (v0.4.0) with Vulkan backend support.

**Current Development Stage:**  
✅ **Phase 5 (Native Layer Wiring) is functionally complete.** The inference mechanism is fully operational on Windows with Vulkan drivers. CLI application successfully loads models and produces streaming text responses. Linux version build infrastructure is in place but not yet runtime-tested.

---

## 1. System Overview

### 1.1 Positioning and Role

InstantAIGate serves as a standardized interface between computational resources (CPU/GPU via Vulkan) and applications within the Instancium ecosystem. It ensures a **Controlled Data Perimeter**, enabling confidential information processing locally without reliance on external cloud providers.

### 1.2 Key Capabilities

- **Local LLM/VLM Inference**: GGUF model support with native performance
- **Vulkan GPU Acceleration**: Cross-platform GPU offload (Windows/Linux)
- **Multimodal Processing**: Image/audio encoding via mtmd bindings
- **Context Pooling**: Efficient resource reuse with concurrent request handling
- **Hot-Swap Model Loading**: Graceful model switching with request draining
- **OpenAI-Compatible API**: Standardized interface for ecosystem integration

---

## 2. Architectural Principles

### 2.1 Independence by Default
The module operates without mandatory binding to external infrastructure. All critical components (inference, telemetry, model management) function within an isolated perimeter.

### 2.2 Technological Coexistence
Designed for diverse environments:
- Native Windows application
- Windows Service
- Docker container (Linux)

### 2.3 Zero-Trust Local with Scalability
Default configuration binds administrative interfaces to loopback (`127.0.0.1`). Server deployment mode supports authenticated remote access via configuration.

### 2.4 Human-Centric Architecture
Transparent telemetry and management provide full control over model lifecycle and resource consumption.

### 2.5 Native Performance via P/Invoke
Direct integration with `llama.cpp` eliminates HTTP proxy overhead, enabling low-latency Vulkan-accelerated inference.

---

## 3. Module Architecture

### 3.1 High-Level Component Diagram

```
┌─────────────────────────────────────────────────────────────────┐
│                    Hosting Layer                                 │
│  ┌─────────────────────┐  ┌─────────────────────┐               │
│  │   InstantAIGate     │  │   InstantAIGate     │               │
│  │       .Server       │  │        .Cli         │               │
│  │  (ASP.NET Core API) │  │  (Console Test App) │               │
│  └──────────┬──────────┘  └──────────┬──────────┘               │
└─────────────┼─────────────────────────┼──────────────────────────┘
              │                         │
              ▼                         ▼
┌─────────────────────────────────────────────────────────────────┐
│                  InstantAIGate.Core                              │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │  Interfaces (Contracts)                                   │   │
│  │  - IModelManager    - IModelProvider                      │   │
│  │  - IBackendFacade   - IVisionFacade                       │   │
│  │  - IInferenceEngine - IModelPathProvider                  │   │
│  └──────────────────────────────────────────────────────────┘   │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │  Services (Business Logic)                                │   │
│  │  - ModelManager (lifecycle, leasing, metrics)             │   │
│  │  - ModelProvider (pooling, initialization, logging)       │   │
│  │  - RequestQueue (backpressure, pause/resume)              │   │
│  └──────────────────────────────────────────────────────────┘   │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │  DTOs (Data Transfer Objects)                             │   │
│  │  - Config: ModelSettings, InferenceSettings, BackendSettings │
│  │  - Inference: InferenceContext, ChatMessage, InferenceMetrics │
│  │  - Status: ModelRegistryStatus, NativeModelDetails        │   │
│  └──────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────┘
              │
              ▼
┌─────────────────────────────────────────────────────────────────┐
│                InstantAIGate.Native                              │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │  Inference (Facade Layer)                                 │   │
│  │  - BackendFacade (IBackendFacade impl)                    │   │
│  │  - LlamaInference (IInferenceEngine impl)                 │   │
│  │  - VisionFacade (IVisionFacade impl)                      │   │
│  └──────────────────────────────────────────────────────────┘   │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │  Core (High-Level Wrappers)                               │   │
│  │  - LlamaModel (model lifecycle, tokenization)             │   │
│  │  - LlamaContext (inference context, batch ops)            │   │
│  │  - MultiModalContext (image/audio encoding)               │   │
│  └──────────────────────────────────────────────────────────┘   │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │  Bindings (P/Invoke Layer)                                │   │
│  │  - LlamaNative.cs (70+ DllImport functions)               │   │
│  │  - MtmdNative.cs (40+ DllImport functions)                │   │
│  │  - LlamaTypes.cs (enums, structs, constants)              │   │
│  │  - MtmdTypes.cs (multimodal types)                        │   │
│  │  - NativeLibraryLoader.cs (platform detection)            │   │
│  └──────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────┘
              │
              ▼
┌─────────────────────────────────────────────────────────────────┐
│                   Native Runtime Layer                           │
│  ┌─────────────────────┐  ┌─────────────────────┐               │
│  │   llama.dll         │  │   libllama.so       │               │
│  │   (win-x64)         │  │   (linux-x64)       │               │
│  │   Vulkan Backend    │  │   Vulkan Backend    │               │
│  └─────────────────────┘  └─────────────────────┘               │
└─────────────────────────────────────────────────────────────────┘
```

### 3.2 Module Responsibilities

#### InstantAIGate.Core
**Purpose:** Core business logic independent of hosting model.

**Components:**
- **Interfaces/**: Contracts defining inference, telemetry, and model management operations
- **Services/**: Orchestration logic (ModelManager, ModelProvider, RequestQueue)
- **Dtos/**: Configuration models, data transfer objects, status representations

**Key Features:**
- Interface-driven design for testability and modularity
- Immutable configuration objects
- Thread-safe model lifecycle management
- Request queue with backpressure control

#### InstantAIGate.Native
**Purpose:** P/Invoke wrappers for native llama.cpp libraries with Vulkan backend.

**Components:**
- **Bindings/**: Direct C API mappings (low-level DllImports)
  - `LlamaNative.cs`: Backend management, model loading, context handling, vocabulary, sampling
  - `MtmdNative.cs`: Multimodal context, bitmap processing, image/audio encoding
  - `LlamaTypes.cs`: Type definitions (enums, structs, constants)
  - `MtmdTypes.cs`: Multimodal-specific types
  - `NativeLibraryLoader.cs`: Cross-platform dynamic library loading
  
- **Core/**: High-level C# abstractions over native pointers (IDisposable pattern)
  - `LlamaModel.cs`: Model lifecycle and tokenization wrapper
  - `LlamaContext.cs`: Inference context and batch processing
  - `MultiModalContext.cs`: Image/audio encoding via mtmd
  
- **Inference/**: Facade implementations bridging Core interfaces to native calls
  - `BackendFacade.cs`: IBackendFacade implementation
  - `LlamaInference.cs`: IInferenceEngine implementation (streaming generation, tokenization, chat templates)
  - `VisionFacade.cs`: IVisionFacade implementation
  
- **DependencyInjection/**: Service registration extensions

**Key Features:**
- Automatic naming convention: snake_case (C) → PascalCase (C#)
- Proper marshaling: UTF-8 strings, bool as I1, IntPtr for opaque handles
- IDisposable pattern for deterministic native resource cleanup
- Nullable reference types enabled

#### InstantAIGate.SSR (Model Service & Telemetry)
**Purpose:** Model downloading, registry, and telemetry collection.

**Status:** ⚠️ **Stubbed** - Directories exist but contain only `.gitkeep` placeholders.

**Planned Components:**
- **Downloader/**: Resumable model downloads with SHA256 integrity checks
- **Registry/**: Local model catalog and metadata management
- **Telemetry/**: Metrics collection (VRAM, inference speed, errors)

**Current Workaround:** Models must be manually placed; path resolution via `IModelPathProvider` implementation.

#### InstantAIGate.Server
**Purpose:** ASP.NET Core hosting layer providing OpenAI-compatible REST API and SignalR hubs.

**Status:** ⚠️ **Minimal Skeleton** - Basic ASP.NET template without inference integration.

**Planned Components:**
- **Controllers/**: OpenAI-compatible endpoints (`/v1/chat/completions`)
- **Hubs/**: SignalR hubs for telemetry and control
- **Middleware/**: Authentication, CORS, request validation

**Current State:** Placeholder Program.cs with basic MVC setup.

#### InstantAIGate.Cli
**Purpose:** Console application for visual testing and debugging of inference pipeline.

**Status:** ✅ **Fully Functional** - Successfully loads models and streams responses.

**Components:**
- `Program.cs`: Entry point with DI configuration
- `Services/LocalModelPathProvider.cs`: File system-based model path resolution

**Features:**
- Model loading with Qwen3VL-8B-Instruct configuration
- Chat message formatting via native chat templates
- Streaming token generation with stop token detection
- Interactive console output

---

## 4. Data Flow Architecture

### 4.1 Inference Request Flow

```
User Request (CLI/Server)
      │
      ▼
┌─────────────────┐
│  IInferenceEngine │
│  (LlamaInference) │
└────────┬────────┘
         │ StreamGenerationAsync(prompt, settings)
         ▼
┌─────────────────┐
│  IModelManager    │
│  (ModelManager)   │
└────────┬────────┘
         │ AcquireContextAsync(modelId)
         ▼
┌─────────────────┐
│  IModelProvider   │
│  (ModelProvider)  │
└────────┬────────┘
         │ Get from pool OR Create new
         ▼
┌─────────────────┐
│  IBackendFacade   │
│  (BackendFacade)  │
└────────┬────────┘
         │ llama_init_from_model()
         ▼
┌─────────────────┐
│  Native Context   │
│  (llama.cpp)      │
└─────────────────┘
```

### 4.2 Model Loading Flow

```
LoadModelAsync(config)
      │
      ▼
┌─────────────────┐
│  IModelManager    │
│  (thread-safe)    │
└────────┬────────┘
         │
         ▼
┌─────────────────┐
│  IModelPathProvider │
│  (resolve path)     │
└────────┬────────┘
         │
         ▼
┌─────────────────┐
│  IModelProvider   │
│  (initialize)     │
└────────┬────────┘
         │
    ┌────┴────┐
    │         │
    ▼         ▼
┌─────────┐ ┌──────────┐
│Backend  │ │Vision    │
│Facade   │ │Facade    │
│(model)  │ │(projector│
└────┬────┘ └─────┬────┘
     │            │
     └─────┬──────┘
           ▼
    ┌──────────────┐
    │ Native Model │
    │ + Context    │
    └──────────────┘
```

### 4.3 Context Pooling Strategy

```
┌──────────────────────────────────────────────┐
│  ConcurrentDictionary<string,                │
│                     ConcurrentBag<IntPtr>>   │
│  (per-model context pools)                   │
└──────────────────────────────────────────────┘
           │
    ┌──────┴──────┐
    │             │
    ▼             ▼
Acquire      Return
(Context)    (Dispose)
    │             │
    ▼             ▼
┌────────┐   ┌─────────┐
│Pool.Try│   │Clear KV │
│Take()  │   │Cache    │
└────────┘   │Pool.Add │
             └─────────┘
```

---

## 5. Threading and Concurrency Model

### 5.1 Synchronization Primitives

| Component | Primitive | Purpose |
|-----------|-----------|---------|
| ModelManager | `SemaphoreSlim _globalLock` | Exclusive access for model swaps |
| ModelManager | `int _activeLeases` (Volatile.Read) | Thread-safe lease counter |
| ModelProvider | `SemaphoreSlim[] _initLocks` | Per-model initialization locking |
| ModelProvider | `ConcurrentDictionary` caches | Lock-free read/write operations |
| RequestQueue | `SemaphoreSlim` | Backpressure control |

### 5.2 Lease Pattern for Context Management

```csharp
// Acquisition increments counter
Interlocked.Increment(ref _activeLeases);

// Context attaches callback for automatic decrement
context.AttachOnDispose(() => Interlocked.Decrement(ref _activeLeases));

// Graceful drain waits for leases to reach zero
while (Volatile.Read(ref _activeLeases) > 0)
{
    await Task.Delay(100, ct);
}
```

### 5.3 Request Queue Backpressure

- **Pause/Resume**: Controls incoming request flow during model swaps
- **PendingCount**: Exposed for telemetry monitoring
- **Thread-safe counters**: Volatile operations for state visibility

---

## 6. Native Integration Details

### 6.1 P/Invoke Binding Strategy

**Naming Convention:**
- C `llama_function_name()` → C# `LlamaNative.llama_function_name()`
- C `enum llama_vocab_type` → C# `enum LlamaVocabType`
- Prefix mapping: `llama_` → removed, `mtmd_` → `Mtmd`

**Marshaling Rules:**
- Strings: `UTF-8` byte arrays with explicit length
- Booleans: `I1` (1-byte integer)
- Opaque handles: `IntPtr`
- Callbacks: Delegated function pointers

### 6.2 Sampler Chain Configuration

```csharp
// Intentionally omitting penalty samplers for Instruct models
LlamaNative.llama_sampler_chain_add(sampler, 
    LlamaNative.llama_sampler_init_top_k(settings.TopK));
LlamaNative.llama_sampler_chain_add(sampler, 
    LlamaNative.llama_sampler_init_top_p(settings.TopP, 1));
LlamaNative.llama_sampler_chain_add(sampler, 
    LlamaNative.llama_sampler_init_temp(settings.Temperature));
LlamaNative.llama_sampler_chain_add(sampler, 
    LlamaNative.llama_sampler_init_dist(seed));
```

### 6.3 Stop Token Detection

Hardware-level detection includes model-specific tokens:
```csharp
if (token == eos || token < 0 || token == 151645 || token == 151643)
{
    break; // Qwen-specific stop tokens
}
```

### 6.4 Logging Integration

Dual-channel logging for native operations:
1. **Callback-based**: `llama_log_set()` → `ILogger`
2. **stderr redirection**: `Console.SetError()` → custom `TextWriter`

---

## 7. Deployment Architecture

### 7.1 Windows Deployment

**Option A: Standalone Application**
```
InstantAIGate.Cli.exe
├── InstantAIGate.*.dll
├── runtimes/win-x64/
│   ├── llama.dll
│   ├── ggml.dll
│   └── Vulkan backend DLLs
└── config/
    ├── appsettings.json
    └── models/ (manual placement)
```

**Option B: Windows Service**
```powershell
# Via InstallService.ps1
.\InstallService.ps1 -ServiceName "InstantAIGate"
```

### 7.2 Linux Deployment (Docker)

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY . .
ENTRYPOINT ["dotnet", "InstantAIGate.Server.dll"]
```

**Build Process:**
```bash
./runtimes/build/start.sh
# Produces: runtimes/linux-x64/libllama.so (+ symlinks)
```

**Symlink Strategy:**
- Base files: `libllama.so`, `libggml.so`
- Versioned symlinks: `libllama.so.0`, `libggml.so.0`
- MSBuild post-build target recreates symlinks in output directory

### 7.3 Configuration Perimeter

```json
{
  "Network": {
    "Host": "127.0.0.1",
    "Port": 5000,
    "AllowRemoteAccess": false,
    "RequireAuthentication": true
  }
}
```

**Security Modes:**
- **Local Mode** (default): Loopback-only binding, no remote access
- **Server Mode**: `0.0.0.0` binding with authentication required

---

## 8. Current Implementation Status

### 8.1 Completed Components (Phase 5)

| Module | Status | Notes |
|--------|--------|-------|
| Core.Interfaces | ✅ Complete | All contracts defined |
| Core.Services.ModelManager | ✅ Complete | Thread-safe, lease-tracking |
| Core.Services.ModelProvider | ✅ Complete | Context pooling, logging |
| Core.Services.RequestQueue | ✅ Complete | Backpressure control |
| Native.Bindings | ✅ Complete | 110+ DllImport functions |
| Native.Inference.BackendFacade | ✅ Complete | Full IBackendFacade impl |
| Native.Inference.LlamaInference | ✅ Complete | Streaming, tokenization, templates |
| Native.Inference.VisionFacade | ✅ Complete | mtmd integration |
| Native.DependencyInjection | ✅ Complete | Service registration |
| Cli | ✅ Complete | Functional test harness |

### 8.2 Partially Implemented Components (Phase 4)

| Module | Status | Notes |
|--------|--------|-------|
| Native.Core.LlamaModel | ⚠️ Present | Low-level wrapper, not used by high-level flow |
| Native.Core.LlamaContext | ⚠️ Present | Low-level wrapper, not used by high-level flow |
| Native.Core.MultiModalContext | ⚠️ Present | Wrapper exists, integration pending |
| ModelProvider.VisionSupport | ⚠️ Partial | Context loading works, pooling not implemented |

### 8.3 Not Yet Implemented (Phases 3-2)

| Component | Phase | Priority |
|-----------|-------|----------|
| SSR.Downloader | N/A | Low |
| SSR.Registry | N/A | Low |
| SSR.Telemetry | N/A | Medium |
| Server.Controllers | N/A | High |
| Server.Hubs (SignalR) | N/A | Medium |
| Circuit Breaker Pattern | Phase 3 | Medium |
| Metrics Collection (percentiles) | Phase 3 | Medium |
| Lock-free Context Acquisition | Phase 2 | High |
| VRAM Monitoring | Phase 2 | Medium |

---

## 9. Known Limitations and Technical Debt

### 9.1 Platform Testing Gap
- **Windows x64**: ✅ Tested and operational
- **Linux x64**: ⚠️ Build scripts ready, runtime untested
- **Vulkan Driver Dependency**: Requires host system Vulkan installation

### 9.2 SSR Module Absence
Model management currently requires manual file placement. Missing capabilities:
- Automated model downloading
- Hash verification for integrity
- Model catalog persistence
- Usage telemetry export

### 9.3 Server Hosting Gap
No production-ready HTTP API exists. Current limitations:
- No OpenAI-compatible endpoints
- No authentication middleware
- No CORS configuration
- No request validation

### 9.4 Error Recovery
Missing resilience patterns:
- No automatic retry on native crashes
- No fallback to CPU on GPU errors
- No circuit breaker for repeated failures

---

## 10. File Structure Reference

```
/workspace
├── docs/
│   ├── ARCHITECTURE.md          # This document
│   ├── ARCHITECTURE_CONCEPT.md  # Conceptual overview
│   ├── CHANGELOG.md             # Development history
│   ├── PHASES_PLAN.md           # Phase tracking
│   └── INSTANCIUM_ENGINEERING_GUIDE.md
├── src/
│   ├── InstantAIGate.Core/      # 15 files (~1,800 LOC)
│   │   ├── Dtos/
│   │   ├── Interfaces/
│   │   └── Services/
│   ├── InstantAIGate.Native/    # 12 files (~2,500 LOC)
│   │   ├── Bindings/
│   │   ├── Core/
│   │   ├── Inference/
│   │   └── DependencyInjection/
│   ├── InstantAIGate.SSR/       # Stub directories
│   ├── InstantAIGate.Server/    # Minimal skeleton
│   └── InstantAIGate.Cli/       # Working test app
├── runtimes/
│   ├── build/                   # Docker build scripts
│   ├── win-x64/                 # Windows binaries (not in repo)
│   └── linux-x64/               # Linux binaries (not in repo)
├── deploy/
│   ├── docker/
│   │   ├── Dockerfile
│   │   └── docker-compose.yml
│   └── windows-service/
│       └── InstallService.ps1
└── config/
    ├── appsettings.json
    └── model_catalog.json
```

**Statistics:**
- Total C# files: 37
- Total lines of code: ~5,395
- Native bindings: 110+ DllImport functions

---

## 11. Dependencies

### 11.1 Runtime Requirements

| Dependency | Version | Purpose |
|------------|---------|---------|
| .NET SDK | 8.0+ | Build and runtime |
| llama.cpp | v0.4.0 | Inference engine |
| Vulkan Runtime | Latest | GPU acceleration |
| Visual C++ Redistributable | Latest (Windows) | Native DLL dependencies |

### 11.2 NuGet Packages

```xml
<PackageReference Include="Microsoft.Extensions.Logging" Version="8.0.*" />
<PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="8.0.*" />
<!-- Server-specific -->
<PackageReference Include="Microsoft.AspNetCore.OpenApi" Version="8.0.*" />
```

---

## 12. Next Steps

### Immediate (Phase 5 Completion Validation)
1. ✅ CLI inference testing on Windows
2. ⏳ Linux runtime testing with Vulkan drivers
3. ⏳ Multimodal (vision) end-to-end validation

### Short-Term (Phase 4 Completion)
1. Implement vision context pooling
2. Add image preprocessing pipeline
3. Create multimodal integration tests
4. Optimize batch inference with backpressure

### Medium-Term (Phase 3)
1. Implement server controllers (OpenAI API compatibility)
2. Add SignalR telemetry hubs
3. Develop circuit breaker for GPU errors
4. Create comprehensive metrics collection

### Long-Term (Phase 2)
1. Lock-free context acquisition optimization
2. Async context pre-warming
3. Memory pressure handling
4. VRAM monitoring and limits

---

## Appendix A: Interface Contracts

### IBackendFacade
```csharp
void LoadAllBackends();
void BackendInit();
void BackendFree();
bool SupportsGpuOffload();
IntPtr LoadModel(string path, int gpuLayers, ...);
IntPtr CreateContext(IntPtr modelPtr, uint nCtx, ...);
void FreeModel(IntPtr modelPtr);
void FreeContext(IntPtr ctxPtr);
void SetLogCallback(BackendLogCallback callback);
```

### IInferenceEngine
```csharp
Task<int[]> TokenizeDataAsync(string modelId, string text, CancellationToken ct);
IAsyncEnumerable<string> StreamGenerationAsync(string modelId, string prompt, ...);
Task<string> ApplyChatTemplateAsync(string modelId, IEnumerable<ChatMessage> messages, ...);
```

### IModelManager
```csharp
Task LoadModelAsync(ModelSettings config, CancellationToken ct);
Task<InferenceContext> AcquireContextAsync(string repoId, CancellationToken ct);
Task<ModelWeights> AcquireModelAsync(string repoId, CancellationToken ct);
Task SwapModelAsync(ModelSettings newConfig, CancellationToken ct);
Task UnloadModelAsync(string repoId, CancellationToken ct);
InferenceMetrics GetMetrics();
```

### IModelProvider
```csharp
Task InitializeAsync(ModelSettings config, CancellationToken ct);
Task<InferenceContext> GetInferenceContextAsync(string repoId, CancellationToken ct);
Task<ModelWeights> GetWeightsAsync(string repoId, CancellationToken ct);
void UnloadModel(string repoId);
IEnumerable<ModelRegistryStatus> GetStatus();
```

---

**Document End**
