# Changelog

## [Unreleased] - 2024-XX-XX

### Added
- **InstantAIGate.Native** module with complete P/Invoke bindings for `llama.h` and `mtmd.h` (llama.cpp v0.4.0)
- Vulkan backend support for GPU acceleration on Windows x64 and Linux x64 platforms
- Low-level binding layer (`Bindings/`):
  - `LlamaNative.cs` - 70+ DllImport functions for backend management, model loading, context handling, vocabulary, sampling
  - `MtmdNative.cs` - 40+ DllImport functions for multimodal context, bitmap processing, image/audio encoding
  - `LlamaTypes.cs` - Complete type definitions: enums (LlamaVocabType, LlamaFType, LlamaRopeType, etc.), structs, constants
  - `MtmdTypes.cs` - Multimodal types: MtmdContextParams, MtmdDecoderPos, image/audio format enums
  - `NativeLibraryLoader.cs` - Cross-platform dynamic library loading with automatic platform detection
- High-level abstraction layer (`Core/`):
  - `LlamaModel.cs` - Model lifecycle management, tokenization/detokenization, metadata access
  - `LlamaContext.cs` - Inference context with batch creation, encode/decode operations
  - `MultiModalContext.cs` - Image/audio encoding wrapper with bitmap helpers
- Automatic naming convention: snake_case (C) → PascalCase (C#) with prefix mapping (llama_ → Llama, mtmd_ → Mtmd)
- Proper marshaling implementation: UTF-8 strings, bool as I1, IntPtr for opaque handles, callback delegates
- IDisposable pattern throughout for deterministic native resource cleanup
- Nullable reference types enabled for improved null safety
- XML documentation on all public APIs

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

### Dependencies
- .NET 6+ required
- Native llama.cpp binaries with Vulkan backend (provided separately in `/runtimes`)
- Vulkan runtime drivers installed on host system

