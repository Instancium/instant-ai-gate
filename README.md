<p align="center">
  <img src="media/ig-logo.png" alt="InstantAIGate logo" height="180" />
</p>

<p align="center">
  <a href="#-quick-start-60s"><img src="https://img.shields.io/badge/GHCR-Available-blue?style=flat-square&logo=github" alt="GitHub Container Registry"></a>
  <img src="https://img.shields.io/badge/Hardware-CPU%20%26%20GPU-flash?style=flat-square" alt="Hardware Support">
  <img src="https://img.shields.io/badge/API-OpenAI%20Compatible-orange?style=flat-square" alt="OpenAI API">
  <img src="https://img.shields.io/badge/license-Apache%202.0-green?style=flat-square" alt="License">
</p>

InstantAIGate is the foundational infrastructure developed by independent R&D laboratory Instancium for building autonomous digital products and enterprise-grade AI solutions.

Engineered as a high-throughput gateway, InstantAIGate delivers a compiled, cross-platform .NET 10 architecture with direct memory bindings to native inference engines. This gateway is designed to provide **Architectural Autonomy** and **Vendor Independence**, empowering individuals, professionals, and organizations to host critical AI processes internally. By maintaining absolute control over the infrastructure lifecycle and eliminating forced lock-in to proprietary cloud providers, it ensures that both independent creators and businesses can preserve their **Digital Subjectivity**.

## Core Architecture & Engineering Principles

*   **Enterprise-Grade Runtime (.NET 10):** Built as a robust C# application, InstantAIGate provides predictable deployment across platforms and supports native execution as a background Windows Service. The integration relies on direct P/Invoke bindings (`NativeLibraryLoader`) to `llama.cpp` and `mtmd`.
  
*   **Dual-Port Security:** To enforce strict security boundaries, the gateway implements `PortRoutingMiddleware`. It isolates the public inference endpoint (OpenAI-compatible) from the administrative endpoint across two distinct network ports on the same running instance. This guarantees that management commands are completely inaccessible from the public-facing API, enforcing a strict **Controlled Data Perimeter** for both personal privacy and corporate secrets.
*	**Real-Time Observability & Telemetry:** InstantAIGate incorporates a dedicated SignalR-based telemetry hub (`/hub/telemetry`) that operates strictly within the secure administrative network perimeter. Driven by a dedicated background broadcaster (`MetricsBroadcasterWorker`), it streams high-fidelity diagnostic data at 1Hz—including active VRAM context leases, dynamic request queue backpressure (`InferenceMetrics`), and live server-side download progress (`DownloadProgress`). Furthermore, the gateway dynamically intercepts and pipes native C++ standard error streams directly to connected clients via a custom `SignalRLoggerProvider`.
*   **Dynamic Queueing:** InstantAIGate features a `DynamicRequestQueue` that enforces strict concurrency limits via semaphore leases. This mechanism prevents VRAM overflow and systematically manages execution slots during traffic spikes, ensuring that critical functions remain available under high load.
*   **Zero-Downtime Hot-Swapping:** The `ModelManager` supports graceful hot-swapping (`SwapModelAsync`) via the secure admin endpoint. System administrators and individual researchers can dynamically transition the gateway to a different LLM or VLM without dropping active connections or restarting the service, enabling uninterrupted **Technological Coexistence** of different models.
*   **Interoperability & Right to Exit:** The `/v1/chat/completions` endpoint maps directly to standard OpenAI contracts (`OpenAiChatMessageDto`). Developers, professionals, and enterprises can seamlessly redirect their existing software stack to this local **Sovereign Node** without rewriting client code, ensuring independent deployment and mitigating centralized cloud lock-in risks.
*   **Unified Infrastructure Mediator:** InstantAIGate is continuously evolving as a central integration layer for diverse AI workloads. Beyond LLMs and VLMs, the architecture is designed to incorporate additional analytical engines, including ONNX, YOLO, and OCR models. By acting as a single infrastructure mediator, it enables creators and enterprises alike to dynamically route workflows and rapidly switch between required technologies.

