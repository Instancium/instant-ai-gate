<p align="center">
  <img src="media/ig-logo.png" alt="InstantAIGate logo" height="180" />
</p>

<p align="center">
  <a href="#-quick-start-60s"><img src="https://img.shields.io/badge/GHCR-Available-blue?style=flat-square&logo=github" alt="GitHub Container Registry"></a>
  <img src="https://img.shields.io/badge/Hardware-CPU%20%26%20GPU-flash?style=flat-square" alt="Hardware Support">
  <img src="https://img.shields.io/badge/API-OpenAI%20Compatible-orange?style=flat-square" alt="OpenAI API">
  <img src="https://img.shields.io/badge/license-Apache%202.0-green?style=flat-square" alt="License">
</p>


**InstantAIGate** is a high-throughput, deterministic AI inference gateway developed by **Instancium**. Engineered as a compiled, cross-platform **.NET 10** architecture with direct memory bindings to native inference engines, it enables **Architectural Autonomy** and **Vendor Independence**.

By deploying InstantAIGate internally, organizations and individual creators maintain absolute control over their infrastructure lifecycle, eliminating forced lock-in to proprietary cloud AI providers and preserving vital Digital Subjectivity.

> **Note:** Unlike stateless proxies, InstantAIGate operates as a **stateful inference orchestration gateway**. It anchors inference sessions directly to hardware resources in VRAM, eliminating redundant serialization layers and providing fine-grained, low-level control over KV-cache allocations.

## Key Features

### 1. Direct KV-Cache Control & Manipulation
Gain deterministic programmatic control over the physical KV cache allocated in GPU VRAM:
- **Prefix Checkpointing (`RollbackSession`):** Instant rollback to fixed token boundaries without re-evaluating preceding prefixes.
- **Selective Sequence Shifting (`ShiftSessionCache`):** Sliding window operations in native memory. Excise intermediate turns while preserving system prompts and visual projection tokens.
- **Prefix Tree Context Pooling:** Maximize throughput by reusing shared token prefixes across concurrent sessions.

### 2. Zero-Mutation Policy & Fail-Safe Guard
Strict memory management ensures reliability:
- The gateway *never* silently truncates or summarizes session context.
- **Fail-Safe Guard:** Validates token capacity *before* native decoding routines are called. If capacity is exceeded, it halts execution and throws a `ContextOverflowException`, preventing memory corruption and engine crashes.

### 3. Native State Protocol (SignalR)
High-performance, full-duplex transport (`/hub/chat`):
- **Incremental Delta Transmission:** Transmit only latest prompt deltas, minimizing payload size.
- **Session Persistence:** Context handles remain leased in VRAM across turns for minimal Time-To-First-Token (TTFT).
- **Single-Pass Media Tokenization:** Image embeddings are computed once and pinned in the KV cache, enabling multi-turn conversations without repeated binary transfers.

### 4. Vulkan-Powered Cross-Platform Acceleration
Built on direct memory bindings to native runtimes like `llama.cpp` and `mtmd`. Support for:
- Vulkan-accelerated GPU offloading.
- Unified CPU and hardware compute layers across diverse operating systems and heterogeneous GPU environments.

### 5. Real-Time Observability & Telemetry
Dedicated, secure telemetry hub (`/hub/telemetry`) streaming high-fidelity data at 1Hz:
- VRAM context leases and request queue backpressure (`InferenceMetrics`).
- Live server-side download progress.
- Native C++ `stderr` interception piped directly to SignalR clients.

## Architecture

InstantAIGate utilizes a tiered architecture separating client logic, state management, and native execution.

<p align="center">
  <img src="media/architecture-diagram.jpg" alt="InstantAIGate Architecture Diagram" width="800"/>
</p>

### Main Components:

1.  **InstantAIGate.Cli:** Handles Token Budgeting, Checkpointed Memory Ingestion (`RollbackSessionAsync`), and Sliding Window management.
2.  **InstantAIGate.Server (SignalR Hub):** Manages connection lifecycles, maps `SessionId` to `Context Handle`, handles Delta Ingestion, and enforces the Fail-Safe Guard.
3.  **Core / Native Engine Boundary:** Manages the dynamic request queue with backpressure, Prefix Tree Context Pool, and performs Native Memory Sequence Operations (via `llama.cpp` / `mtmd`).

---

> **Vulkan-Powered Cross-Platform Acceleration:**
> Powered by `llama.cpp` with native Vulkan backend integration, InstantAIGate breaks free from vendor lock-in. It delivers hardware-accelerated LLM/VLM inference across a vast spectrum of consumer and enterprise GPUs without requiring heavy proprietary stacks like CUDA. Supported hardware and environments include:
> * **NVIDIA GPUs** (GeForce, Quadro, Tesla via Vulkan ICD)
> * **AMD Radeon GPUs** (RX series, Vega, RDNA architectures)
> * **Intel Arc & Integrated Graphics** (Xe architecture)
> * **Apple Silicon** (via cross-compilation/Metal-Vulkan translation layers where applicable)
> * **Cross-Environment:** Seamless execution on Windows, Linux, and edge devices within your private network perimeter.
