# Test Coverage Plan - InstantAIGate Project
**Creation Date:** 2025
**Objective:** Capture current project functionality before large-scale refactoring
**Role:** Senior SDET (Software Development Engineer in Test)

---

## 📋 EXECUTIVE SUMMARY

### Project Architecture
The project implements **Clean Architecture** with layered separation:
- **Domain** - entities and value objects
- **Application** - interfaces and DTOs
- **Infrastructure** - inference, storage, telemetry implementation
- **API** - OpenAI-compatible REST API controllers
- **Admin** - Razor Pages administrative panel

### Technology Stack
- **.NET 10** (cutting edge)
- **ASP.NET Core** (Web API + Razor Pages)
- **Native interop** (P/Invoke for llama.cpp)
- **SignalR** (real-time telemetry)
- **OpenAI SDK** (client)

---

## 🎯 TESTING STRATEGY

### Coverage Priorities
1. **CRITICAL** - Core inference pipeline (chat, embeddings)
2. **HIGH** - API controllers (OpenAI compatibility)
3. **HIGH** - Model management (lifecycle, concurrency)
4. **MEDIUM** - Storage operations (download, checksum)
5. **MEDIUM** - Telemetry service
6. **LOW** - UI Razor Pages (integration tests)

### Test Types
- **Unit Tests** - isolated components with mocks
- **Integration Tests** - filesystem and real model interaction (optional)
- **API Tests** - HTTP endpoints (WebApplicationFactory)
- **Contract Tests** - OpenAI API compatibility

---

## 📦 TASK 1: DOMAIN LAYER

### 1.1 Entities Tests (`InstantAIGate.Domain.Tests`)

#### Scope
- `ModelFile` entity lifecycle
- `ConversationMessage` value object
- `ModelReference` validation
- `BackendType` enum handling

#### Test Files
- `ModelFileTests.cs` - Entity lifecycle and state management
- `ConversationMessageTests.cs` - Value object immutability
- `ModelReferenceTests.cs` - Reference resolution and validation
- `BackendTypeTests.cs` - Enum conversions and comparisons

#### Key Scenarios
1. ModelFile creation and state transitions
2. Message content encoding/decoding
3. Reference equality and hashing
4. Backend type classification (CPU vs GPU)

---

## 📦 TASK 2: APPLICATION LAYER

### 2.1 DTO Validation Tests

#### Scope
- Input DTO validation (Create, Update requests)
- Output DTO serialization
- OpenAI SDK integration DTOs

#### Test Coverage
- Required field validation
- Enum constraint validation
- Numeric range validation (temperature, top_p)
- Collection size limits
- Custom validation rules

### 2.2 Application Service Tests

#### Scope
- `ChatCompletionService` - request handling, model selection
- `EmbeddingService` - batch processing
- `ModelManager` - lifecycle operations
- `TelemetryService` - event aggregation

#### Key Scenarios
- Request routing to correct backend
- Model auto-selection when not specified
- Batch processing with timeout handling
- Event filtering and aggregation

---

## 📦 TASK 3: INFRASTRUCTURE LAYER

### 3.1 Inference Engine Tests (COMPLETED ✅)

**Status**: 84/84 tests passing
**Execution Time**: 0.8s

#### NativeLibraryLoaderTests.cs (14 tests)
- Constructor initialization
- State validation (IsLoaded, CurrentBackend)
- Exception handling
- Backend type validation
- Debug logging behavior
- Idempotent loading
- Cross-platform library naming
- Thread-safety for concurrent loads

#### NativeBackendRegistryTests.cs (24 tests)
- Backend enumeration (all vs available)
- Case-insensitive lookup
- Auto backend resolution (GPU > CPU)
- Missing backend null handling
- Refresh consistency
- Backend priority logic
- RID pattern validation
- Thread-safe concurrent reads

#### RuntimeExtractorTests.cs (20 tests)
- 7z archive detection
- Extraction to target directory
- Existing file handling (skip/overwrite)
- Permission preservation (Unix)
- Corrupted archive handling
- Path traversal protection
- Concurrent extraction thread-safety

#### BackendInitializationTests.cs (26 cross-platform tests)
- Runtime identifier detection
- Platform-specific library naming
- Path separator handling
- Graceful degradation in Docker
- Backend discovery robustness
- Environment variable handling

### 3.2 Storage Tests

#### Scope
- Model file download with progress
- Checksum validation (SHA256)
- Concurrent download handling
- Disk space validation
- Atomic move operations

### 3.3 Telemetry Tests

#### Scope
- Event aggregation
- SignalR message formatting
- Sampling logic
- Buffer overflow handling

---

## 📦 TASK 4: API LAYER

### 4.1 OpenAI Compatibility Tests

#### Scope
- `/v1/chat/completions` endpoint
- `/v1/embeddings` endpoint
- Request/response format validation
- Error response codes

#### Test Coverage
- Exact request/response schema match
- Streaming response format
- Error handling (model not found, overload)
- Rate limiting headers

### 4.2 Health Check Tests

#### Scope
- `/health` endpoint
- Model availability check
- Backend status reporting

---

## 📦 TASK 5: RAZOR PAGES (Admin)

### 5.1 Page Model Tests

#### Scope
- `ModelsPage` - model listing, filtering
- `ModelDetailPage` - upload, deletion
- `SettingsPage` - configuration changes

### 5.2 Integration Tests

#### Scope
- File upload handling
- Model installation flow
- Dashboard statistics

---

## ✅ COMPLETION CRITERIA

### Code Quality
- [ ] All tests follow AAA pattern (Arrange, Act, Assert)
- [ ] Descriptive test method names: `MethodName_Scenario_ExpectedResult`
- [ ] No magic numbers or hardcoded values
- [ ] Proper use of assertions (FluentAssertions)
- [ ] No test interdependencies (isolation)

### Coverage Metrics
- [ ] Domain: 90%+ coverage
- [ ] Application: 85%+ coverage
- [ ] Infrastructure.Inference: 95%+ coverage
- [ ] API Controllers: 80%+ coverage

### Documentation
- [ ] Test plan documented (this file)
- [ ] Complex scenarios have comments
- [ ] Test data generation clearly explained
- [ ] Known limitations documented

---

## 🔗 Related Files

- Test infrastructure: `tests/` folder structure
- Mock setup: `tests/Common/Mocks/`
- Test data builders: `tests/Common/Builders/`
- Coverage report: `docs/TEST_COVERAGE_TASK3_PROGRESS.md`

---

**Status:** Work in progress - Task 3 complete, Tasks 4-5 pending
**Last Updated:** 2025-01-XX
