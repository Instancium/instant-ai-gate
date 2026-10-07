# InstantAIGate.Core

`InstantAIGate.Core` provides the fundamental contracts, data transfer objects (DTOs), and interfaces for the InstantAIGate AI inference ecosystem.

## Overview

This package is designed to be shared across various components of the InstantAIGate architecture (such as the CLI, Server, Native wrapper, and standalone tools like InstantPublisher). By referencing this package, you ensure strict contract parity when communicating with the AI Gateway.

### Key Features
* **Inference Contracts:** Standardized `ChatMessage`, `MessageContent`, and streaming deltas (`SessionTokenDelta`).
* **Gateway Status:** Observability models (`GatewayOperationalStatus`, `GatewayStatusDetails`, `InferenceMetrics`).
* **Configuration:** Core configuration models (`StorageSettings`, `ModelSettings`).
* **Hardware Abstractions:** High-level abstractions for AI model execution (`IInferenceEngine`, `IModelProvider`).

## Usage

Reference this package in your .NET project to interact with the InstantAIGate SignalR endpoints or to implement your own gateway clients.

```csharp
// Example: Receiving a streaming delta via SignalR using Core DTOs
connection.On<SessionTokenDelta>("ReceiveTokenDelta", delta =>
{
    Console.Write(delta.Content);
});
```

## License

This project is licensed under the Apache License 2.0 - see the [LICENSE](LICENSE) file for details.