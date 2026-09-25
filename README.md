<p align="center">
  <img src="media/ig-logo.png" alt="InstantAIGate logo" height="180" />
</p>

# InstantAIGate

InstantAIGate is the foundational infrastructure developed by independent R&D laboratory Instancium for building autonomous digital products and enterprise-grade AI solutions.

Engineered as a high-throughput gateway, InstantAIGate delivers a compiled, cross-platform .NET 10 architecture with direct memory bindings to native inference engines. This gateway is designed to provide **Architectural Autonomy** and **Vendor Independence**, empowering individuals, professionals, and organizations to host critical AI processes internally. By maintaining absolute control over the infrastructure lifecycle and eliminating forced vendor lock-in, it ensures that both independent creators and businesses can preserve their **Digital Subjectivity**.

## Core Architecture & Engineering Principles

*   **Enterprise-Grade Runtime (.NET 10):** Built as a robust C# application, InstantAIGate provides predictable deployment across platforms and supports native execution as a background Windows Service. The integration relies on direct P/Invoke bindings (`NativeLibraryLoader`) to `llama.cpp` and `mtmd`.
*   **Dual-Port Security:** To enforce strict security boundaries, the gateway implements `PortRoutingMiddleware`. It isolates the public inference endpoint (OpenAI-compatible) from the administrative endpoint across two distinct network ports on the same running instance. This guarantees that management commands are completely inaccessible from the public-facing API, enforcing a strict **Controlled Data Perimeter** for both personal privacy and corporate secrets.
*   **Dynamic Queueing:** InstantAIGate features a `DynamicRequestQueue` that enforces strict concurrency limits via semaphore leases. This mechanism prevents VRAM overflow and systematically manages execution slots during traffic spikes, ensuring that critical functions remain available under high load.
*   **Zero-Downtime Hot-Swapping:** The `ModelManager` supports graceful hot-swapping (`SwapModelAsync`) via the secure admin endpoint. System administrators and individual researchers can dynamically transition the gateway to a different LLM or VLM without dropping active connections or restarting the service, enabling uninterrupted **Technological Coexistence** of different models.
*   **Interoperability & Right to Exit:** The `/v1/chat/completions` endpoint maps directly to standard OpenAI contracts (`OpenAiChatMessageDto`). Developers, professionals, and enterprises can seamlessly redirect their existing software stack to this local **Sovereign Node** without rewriting client code, ensuring independent deployment and mitigating infrastructure lock-in risks.
*   **Unified Infrastructure Mediator:** InstantAIGate is continuously evolving as a central integration layer for diverse AI workloads. Beyond LLMs and VLMs, the architecture is designed to incorporate additional analytical engines, including ONNX, YOLO, and OCR models. By acting as a single infrastructure mediator, it enables creators and enterprises alike to dynamically route workflows and rapidly switch between required technologies.