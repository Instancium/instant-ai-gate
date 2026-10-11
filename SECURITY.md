# Security Policy

## Supported Versions

InstantAIGate follows a rolling release model. Security patches are applied to the latest stable release and the current development branch (`main`).

| Version | Supported |
| --- | --- |
| Latest stable release | ✅ |
| Current `main` branch | ✅ |
| Older releases | ❌ |

## Reporting a Vulnerability

We take the security of InstantAIGate seriously. If you discover a security vulnerability, please report it **responsibly**.

### How to Report

**Do NOT open a public GitHub issue for security vulnerabilities.**

Please report via one of the following channels:

1. **GitHub Private Vulnerability Reporting** (Preferred): Use the [GitHub Security Advisories](https://github.com/Instancium/instant-ai-gate/security/advisories/new) feature to submit a confidential, encrypted report.
2. **Direct Email**: Send details to **support@instancium.com** with the subject line `[SECURITY] InstantAIGate - <brief description>`.

### What to Include

To help us triage and fix the issue quickly, please provide:
- A clear description of the vulnerability and its potential impact.
- Steps to reproduce or a minimal proof-of-concept (PoC).
- The affected component (e.g., `GatewayHub`, `ApiKeyAuthMiddleware`, `NativeModelValidator`, `ParallelModelDownloader`).
- Suggested fix or mitigation, if available.

### Response Timeline

We commit to the following Service Level Agreement (SLA) for security reports:

| Stage | Timeframe |
| --- | --- |
| Initial acknowledgment | 3 business days |
| Preliminary assessment & triage | 7 business days |
| Fix development & patch release | 14–30 business days (severity dependent) |
| Public disclosure (coordinated) | After the patch is released and users have had time to update |

## Scope

The following components are **in scope** for vulnerability reporting:

- **Gateway Server** (`src/InstantAIGate.Server`): Authentication (`ApiKeyAuthMiddleware`), authorization policies, SignalR hub security, and global exception handling (`GatewayExceptionMiddleware`).
- **Native Runtime Boundary** (`src/InstantAIGate.Native`): Memory safety in P/Invoke calls, buffer overflows in native interop, and `LlamaInference` unsafe code blocks.
- **Model Download Pipeline** (`src/InstantAIGate.SSR`): Integrity validation (`NativeModelValidator`), SSRF prevention, TLS enforcement, and path traversal guards.
- **CLI Client** (`src/InstantAIGate.Cli`): Secure credential handling, command injection vectors in `ProcessRunner`, and release pipeline guardrails.
- **Core Domain** (`src/InstantAIGate.Core`): Session isolation, `ContextOverflowException` guards, queue exhaustion prevention, and `ModelProvider` concurrency.

### Out of Scope

- Vulnerabilities in third-party dependencies (e.g., `llama.cpp`, `SkiaSharp`). Please report these directly to the upstream maintainers; we track them via automated dependency scanning.
- Issues requiring physical access to the host machine or hypervisor escape.
- Denial-of-service (DoS) attacks that require prior authenticated admin access.
- Speculative or theoretical attacks without a working, reproducible proof-of-concept.

## Security Architecture

InstantAIGate is designed with defense-in-depth principles, implemented directly in the codebase:

- **Role-Based API Key Authentication**: All endpoints are protected by `ApiKeyAuthMiddleware`. The system enforces strict separation of privileges using distinct `TenantApiKey` (for inference and session management) and `AdminApiKey` (for gateway administration, model management, and metrics).
- **Zero Information Leakage**: A global `GatewayExceptionMiddleware` intercepts all unhandled exceptions, ensuring that internal stack traces, file paths, or native memory addresses are never exposed to the client in HTTP or SignalR responses.
- **Model Integrity Validation**: Before any GGUF model is loaded into the native inference engine, it undergoes strict header integrity checks via `NativeModelValidator.ValidateIntegrityAsync`. This prevents the execution of corrupted, truncated, or maliciously tampered model weights.
- **Fail-Safe Context Guards**: The native interop layer implements strict bounds checking. If a prompt exceeds the allocated context window, the gateway throws a `ContextOverflowException` *before* dispatching to native routines, preventing memory corruption and engine crashes.
- **Session Isolation**: Each `SessionId` is bound to a dedicated `StatefulSessionEntry` with its own execution gate (`SemaphoreSlim`), preventing cross-session data leakage and race conditions during concurrent inference.
- **Secure Release Pipeline**: Model downloads and version control operations are executed with strict TLS enforcement, redirect limits, and path traversal prevention in `ParallelModelDownloader`.

## Disclosure Policy

We follow [Coordinated Vulnerability Disclosure (CVD)](https://en.wikipedia.org/wiki/Coordinated_vulnerability_disclosure). We ask that you:
- Give us reasonable time to fix the issue before disclosing it publicly.
- Avoid exploiting the vulnerability beyond what is strictly necessary to demonstrate it.
- Respect user privacy and data — do not exfiltrate, modify, or destroy data during testing.

We commit to:
- Not taking legal action against researchers who follow this policy in good faith.
- Acknowledging your contribution in the release notes (unless you prefer to remain anonymous).
- Working with you to verify the fix before any public disclosure.

---

Thank you for helping keep InstantAIGate and its users safe.