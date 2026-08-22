# 📦 InstantAIGate.Core

**InstantAIGate.Core** is the foundational contract library for the entire InstantAIGate AI ecosystem.

There is **NO** business logic, neural network inference algorithms, or dependencies on external ML libraries (like ONNX Runtime, LlamaSharp, etc.) in this project. This library acts as a pure contract SDK that microservices (`Proxy`, `Chat`, `Flow`) use to communicate with each other.

## 🎯 Architectural Principles

1. **Zero Logic:** Contains only Data Transfer Objects (DTOs), interfaces, and exceptions.
2. **Immutable Data:** Network payloads are strictly defined as `record` types with `init`-only properties. This guarantees thread safety and eliminates cumbersome positional constructors.
3. **No External Dependencies:** The project must not rely on heavy NuGet packages. It uses only pure C# and base .NET libraries.

---

## 📂 Project Structure (Functional Modules)

To prevent the project from becoming a cluttered dumping ground, it is divided into technical layers (`Abstractions`, `Models`, `Exceptions`). Inside each technical layer, we create **functional module folders** (`Chat`, `Flow`, `Vision`, etc.).

The name of a functional module is consistently mirrored across every technical layer.

```text
InstantAIGate.Core/
├── Abstractions/                 # Pure interfaces (Contracts)
│   ├── Common/                   # Shared interfaces (IUser, ITelemetry)
│   ├── Chat/                     # Text generation contracts (IInferenceEngine)
│   └── Flow/                     # RAG and pipeline contracts (IDocumentParser)
│
├── DTOs/                         # DTOs - strictly 'record' types
│   ├── Common/                   # Shared models (ErrorResponse)
│   ├── Chat/                     # Chat structures (ChatRequest, ChatResponse, Message)
│   └── Flow/                     # Pipeline structures (GraphNode, DocumentChunk)
│
├── Exceptions/                   # Custom ecosystem exceptions
│   ├── Common/                   # RateLimitException, UnauthorizedException
│   └── Chat/                     # ModelNotLoadedException, ContextTooLongException
│
└── Enums/                        # Constants and enumerations
    └── Chat/                     # RoleType (System, User, Assistant)

```

---

## 🚀 Scaling Strategy (Adding New Features)

The `Core` architecture is designed following the **Open/Closed Principle (OCP)**: the system is open for extension but closed for modification.

When a major new feature is introduced to the ecosystem (e.g., `Speech` recognition), scaling happens as follows:

1. **Isolation:** We do not modify existing `Chat` or `Flow` folders.
2. **Module Creation:** A new unique folder named `Speech` is created across all technical layers:
* `Abstractions/Speech/` gets an `IAudioProcessor`.
* `Models/Speech/` gets an `AudioRequest` and `TranscriptionResponse`.
* `Exceptions/Speech/` gets an `UnsupportedAudioFormatException`.


3. **DTO Versioning:** If the structure of an old API changes (e.g., `ChatRequest` requires new mandatory fields for v2.0), a new model is created (e.g., `ChatRequestV2`) to avoid breaking contracts for existing clients and older gateway versions.

## 📝 Rules for Writing Models (DTOs)

We strictly use `record` types with object initializers instead of positional records. This allows for flexible object creation without giant constructors and enables the use of the `with` keyword for non-destructive mutation in gateway pipelines.

**✅ Correct:**

```csharp
public record ChatRequest
{
    public string Model { get; init; } = "default-model";
    public float Temperature { get; init; } = 0.7f;
    public List<Message> Messages { get; init; } = new();
}

```

**❌ Incorrect (Forbidden):**

```csharp
// Cumbersome constructor, hard to read with 10+ parameters
public record ChatRequest(string Model, float Temperature, List<Message> Messages);

// Mutable class (dangerous during parallel processing in the Proxy)
public class ChatRequest { public string Model { get; set; } }

```
