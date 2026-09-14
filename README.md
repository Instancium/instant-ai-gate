<p align="center">
  <img src="media/ig-logo.png" alt="InstantAIGate logo" height="180" />
</p>

# 🚀 InstantAIGate: Enterprise AI Ecosystem
***

InstantAIGate is a **foundational infrastructure component** of the Instancium R&D lab ecosystem. It provides *Architectural Autonomy* and *Independence by Default* for any other lab solutions requiring local AI inference.

# Module Concept: InstantAIGate

## 1. Positioning and Role in the Ecosystem
**InstantAIGate** is a high-performance, cross-platform module for local inference and AI model management, developed by the Instancium R&D lab. 

It serves as a standardized interface (*Interoperability*) between computational resources (CPU/GPU via Vulkan) and applications within the Instancium ecosystem. The module ensures a *Controlled Data Perimeter*, allowing lab solutions to process confidential information locally, reducing reliance on external cloud providers and minimizing data leakage risks (*Architectural Risk Reduction*).

## 2. Key Architectural Principles
1. **Independence by Default:** The module is capable of functioning without mandatory binding to external infrastructure. All critical components (downloading, inference, telemetry) operate within an isolated perimeter.
2. **Technological Coexistence:** The solution is designed to operate across diverse environments: as a native Windows application, a Windows Service, and an isolated Docker container in a Linux environment.
3. **Zero-Trust Local with Scalability:** By default, all administrative interfaces (SignalR, management API) are strictly bound to the loopback interface (`127.0.0.1`) to minimize the *Reduced Attack Surface*. However, the architecture provides a configuration mode for server deployment, where access is regulated via authentication and encryption mechanisms, preserving the *Controlled Data Perimeter*.
4. **Human-Centric Architecture:** The module provides transparent telemetry and management, allowing an engineer or administrator to maintain full control over the model lifecycle and resource consumption.

## 3. Module Objectives
* Provide other Instancium solutions with a unified, standardized API (OpenAI-compatible) for working with local LLM/VLM models (GGUF).
* Ensure native, high-performance operation with multimodal models (e.g., Qwen-VL) through direct calls (P/Invoke) to the `llama.cpp` core, bypassing redundant in-process HTTP proxies.
* Implement a built-in mechanism for secure downloading, versioning, and caching of models (SSR library) without dependency on external package managers.

---

# InstantAIGate Project Structure
```text
InstantAIGate.sln
│
├── src/
│   ├── InstantAIGate.Core/               # Core business logic, independent of hosting model
│   │   ├── Interfaces/                   # Contracts for inference, telemetry, and model management
│   │   ├── Models/                       # Data transfer objects and configuration models
│   │   └── Services/                     # Core orchestration logic (e.g., ModelOrchestrator)
│   │
│   ├── InstantAIGate.Native/             # P/Invoke wrappers for native llama.cpp libraries
│   │   ├── Bindings/                     # Direct C API mappings (llama.h, mtmd.h)
│   │   ├── Core/                         # High-level C# abstractions over native pointers (IDisposable)
│   │   └── Sampling/                     # Sampler chain management and token processing
│   │
│   ├── InstantAIGate.SSR/                # Model Service & Telemetry library
│   │   ├── Downloader/                   # Resumable model downloading with integrity checks (SHA256)
│   │   ├── Registry/                     # Local model catalog and metadata management
│   │   └── Telemetry/                    # Metrics collection (VRAM, inference speed, errors)
│   │
│   ├── InstantAIGate.Server/             # ASP.NET Core hosting layer (API + SignalR)
│   │   ├── Controllers/                  # OpenAI-compatible REST endpoints (/v1/chat/completions)
│   │   ├── Hubs/                         # SignalR hubs for telemetry and control (configurable binding)
│   │   └── Middleware/                   # Authentication, CORS, and request validation
│   │
│   └── InstantAIGate.Cli/                # Console application for visual testing and debugging
│       └── Commands/                     # CLI commands for chat, telemetry, and model ops
│
├── runtimes/                             # Precompiled native binaries (not compiled by .NET)
│   ├── win-x64/                          # Windows native libraries (llama.dll, Vulkan backends)
│   └── linux-x64/                        # Linux native libraries (libllama.so, Vulkan backends)
│
├── deploy/                               # Deployment artifacts and configurations
│   ├── docker/
│   │   ├── Dockerfile                    # Multi-stage build for Linux container deployment
│   │   └── docker-compose.yml            # Example composition with volume mapping for models
│   └── windows-service/
│       └── InstallService.ps1            # Script to register InstantAIGate.Server as a Windows Service
│
└── config/
    ├── appsettings.json                  # Main configuration (includes NetworkBinding, SSR paths)
    └── model_catalog.json                # Default model registry and launch configurations
```

---

### 1. Deployment Flexibility (Technological Coexistence)
The module is designed as a cross-platform solution:
* **Windows Service:** Allows integration into corporate workstations or Windows servers, ensuring automatic startup and background operation without an active user session.
* **Docker Container:** Ensures *Interoperability* (infrastructure replaceability). The container includes all necessary dependencies (including Vulkan drivers via passthrough or CPU-fallback), allowing deployment on any Linux server within a cluster.

### 2. Network Security and Configuration (Architectural Risk Reduction)
Instead of hardcoding `localhost`, the configuration (`appsettings.json`) manages the network perimeter:
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
* **By Default:** `Host` = `127.0.0.1`, `AllowRemoteAccess` = `false`. This implements the *Zero-Trust Local* principle, guaranteeing that administrative APIs and telemetry are inaccessible from the outside.
* **Server Mode:** When deployed in Docker or as a dedicated service, an administrator can change the `Host` to `0.0.0.0` and enable `RequireAuthentication`. This transforms the module into a secure internal microservice, maintaining a *Controlled Data Perimeter* within the corporate network.

### 3. SSR (Model Service & Telemetry) as an Ecosystem Standard
The SSR library guarantees that any Instancium application using InstantAIGate receives:
* **Resumable Downloads:** The ability to resume downloading large GGUF files after connection drops.
* **Integrity Verification:** Hash sum verification to prevent the execution of corrupted or tampered models.
* **Local Telemetry Sink:** Collection of metrics (generation speed, VRAM usage) with the ability to export to local files or transmit via a secure SignalR channel, without sending data externally (*Zero Data Leak*).

***

