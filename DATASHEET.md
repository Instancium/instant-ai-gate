# 📊 InstantAIGate Technical Data Sheet

This document provides system administrators, DevOps engineers, and IT specialists with strict hardware, software, and driver requirements for deploying **InstantAIGate**.

---

## 💻 Hardware Support Matrix

### 🟢 NVIDIA GPU Backend (Highly Recommended)
The native core is built with compute architectures spanning from **Ampere** to the latest generation architecture chips.
* **Supported Architectures (`CMAKE_CUDA_ARCHITECTURES`):** * `86` — Ampere (RTX 30xx, A100, A30, A40, A10)
  * `89` — Ada Lovelace (RTX 40xx, L4, L40)
  * `90` — Hopper (H100, H200)
  * `100` — Blackwell (B100, B200)
  * `120` — Rubin (Next-gen architecture)
* **VRAM Overhead:** Base footprint is extremely light (< 200MB for the gateway instance). Total VRAM allocation depends entirely on the selected GGUF model size, context length (`MaxContexts`), and KV-Cache quantization settings.

### ⚙️ CPU Backend (Automatic Fallback)
* **Supported Architectures:** x86-64 with AVX2 instruction sets.
* **RAM Requirement:** Model size in GB + 20% buffer for context window overhead.
* **Threading Policy:** Automatically scales to use available physical CPU cores.

---

## 💿 Software & Driver Requirements

### 🐧 Linux Environment (Containerized / On-Prem)
* **Base OS:** Ubuntu 22.04 LTS (or any glibc-compatible modern Linux distribution).
* **NVIDIA Driver:** Requires CUDA **12.8** compatible drivers (Minimum Host Driver Version: **570.xx+** or newer matching CUDA 12.8.x toolkits).
* **Container Runtime:** Docker with `nvidia-container-toolkit` installed and configured for GPU scheduling inside `docker-compose.yml`.

### 🪟 Windows Environment (Native)
* **Base OS:** Windows Server 2022 / Windows 10 & 11 (64-bit).
* **NVIDIA Driver:** Official NVIDIA Display Driver with CUDA 12.8 support.

---

## 🛠️ Build Specs & Core Engine Integrity

The gateway utilizes a highly optimized, stripped-down custom compilation of the `llama.cpp` core (Commit: `6e14286`) to minimize attack surface and binary weight:

* **Active Features:**
  * `GGML_CUDA=1` (Hardware accelerated tensor math via CUDA)
  * Custom Host Compiler: `g++-12` for strict alignment optimization.
* **Compiled Artifacts Included:** `libllama.so`, `libggml-cuda.so`, `libggml-base.so`, `libggml-cpu.so`.
* **Excluded Ecosystem Components:** All standard executable wrappers (`llama-cli`, `llama-server`, benchmarks, and test binaries) are stripped at compilation level to guarantee zero unmanaged process leaks.

---

## ❌ Current Limitations & Out-of-Scope (As of v0.9.0)

To keep implementation predictable and prevent resource allocation conflicts, the following features are **explicitly not supported** in the current build:

| Feature / Tech | Current Status | Alternative / Target Roadmap |
| :--- | :--- | :--- |
| **VULKAN Backend** | 🚫 **Not Supported** | Scheduled for next major release (v1.1.0) for AMD/Intel GPU compatibility. |
| **AMD ROCm Backend** | 🚫 **Not Supported** | Under evaluation. Use CPU-AVX2 fallback on AMD EPYC platforms. |
| **OpenAI Vision / Audio** | 🚫 **Not Supported** | Protocol schemas are ready, but underlying compute pipelines accept **Text Only** inputs. |