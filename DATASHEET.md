<p align="center">
  <img src="media/ig-logo.png" alt="InstantAIGate logo" height="180" />
  <br />
  <strong>High-Performance. Self-Hosted. Zero Setup.</strong>
  <br />
  A lightweight .NET middleware providing a monitored, shared foundation for local AI applications.
</p>

<p align="center">
  <a href="README.md"><b>Overview</b></a> │ 
  <a href="INSTALLATION.md"><b>Installation Guide</b></a> │ 
  <a href="DATASHEET.md"><b>Technical Data Sheet</b></a>
</p>

<p align="center">
  <a href="#-quick-start-60s"><img src="https://img.shields.io/badge/GHCR-Available-blue?style=flat-square&logo=github" alt="GitHub Container Registry"></a>
  <img src="https://img.shields.io/badge/Hardware-CPU%20%26%20GPU-flash?style=flat-square" alt="Hardware Support">
  <img src="https://img.shields.io/badge/API-OpenAI%20Compatible-orange?style=flat-square" alt="OpenAI API">
  <img src="https://img.shields.io/badge/Architecture-DDD-purple?style=flat-square" alt="DDD">
  <img src="https://img.shields.io/badge/license-Apache%202.0-green?style=flat-square" alt="License">
</p>

---

# InstantAIGate Technical Data Sheet

This document provides system administrators, DevOps engineers, and IT specialists with strict hardware, software, and driver requirements for deploying **InstantAIGate**.

## Hardware Support Matrix

### 🟢 NVIDIA GPU Backend (Highly Recommended)
The native core is built with compute architectures spanning from **Ampere** to the latest generation architecture chips.
* **Supported Architectures (`CMAKE_CUDA_ARCHITECTURES`):** * `86` — Ampere (RTX 30xx, A100, A30, A40, A10)
  * `89` — Ada Lovelace (RTX 40xx, L4, L40)
  * `90` — Hopper (H100, H200)
  * `100` — Blackwell (B100, B200)
  * `120` — Blackwell Consumer (RTX 50xx)
* **VRAM Overhead:** Base footprint is extremely light (< 200MB for the gateway instance). Total VRAM allocation depends entirely on the selected GGUF model size, context length (`MaxContexts`), and KV-Cache quantization settings.

### CPU Backend (Automatic Fallback)
* **Supported Architectures:** x86-64 with AVX2 instruction sets.
* **RAM Requirement:** Model size in GB + 20% buffer for context window overhead.
* **Threading Policy:** Automatically scales to use available physical CPU cores.

## Software & Driver Requirements

**⚠️ CRITICAL REQUIREMENT:** The host system's NVIDIA drivers MUST be updated to support a minimum of CUDA 13.3.0. Older drivers will cause initialization failures.

### 🐧 Linux Environment (Containerized / On-Prem)
* **Base OS:** Ubuntu 22.04 LTS (or any glibc-compatible modern Linux distribution).
* **NVIDIA Driver:** Requires host driver updates to support **CUDA 13.3.0**.
* **Container Runtime:** Docker with `nvidia-container-toolkit` installed and configured for GPU scheduling inside `docker-compose.yml`.

### Windows Environment (Native)
* **Base OS:** Windows Server 2022 / Windows 10 & 11 (64-bit).
* **NVIDIA Driver:** Official NVIDIA Display Driver updated to support **CUDA 13.3.0**.

## Build Specs & Core Engine Integrity

The gateway utilizes a highly optimized, stripped-down custom compilation of the `llama.cpp` core (Commit: `6e14286`) to minimize attack surface and binary weight:

* **Active Features:**
  * `GGML_CUDA=1` (Hardware accelerated tensor math via CUDA)
  * Custom Host Compiler: `g++-12` for strict alignment optimization.
* **Compiled Artifacts Included:** `libllama.so`, `libggml-cuda.so`, `libggml-base.so`, `libggml-cpu.so`.
* **Excluded Ecosystem Components:** All standard executable wrappers (`llama-cli`, `llama-server`, benchmarks, and test binaries) are stripped at compilation level to guarantee zero unmanaged process leaks.

## ❌ Current Limitations & Out-of-Scope (As of v1.0.1)

To keep implementation predictable and prevent resource allocation conflicts, the following features are **explicitly not supported** in the current stable build:

| Feature / Tech | Current Status in v1.0.1 | Target Release | Alternative / Workaround |
| :--- | :--- | :--- | :--- |
| **VULKAN Backend** | 🚫 Not Supported | 🚀 **v1.1.0** | Scheduled for AMD/Intel GPU native acceleration support. |
| **AMD ROCm Backend** | 🚫 Not Supported | 🚀 **v1.1.0** | Under evaluation. Use CPU-AVX2 fallback on AMD EPYC platforms. |
| **OpenAI Vision / Audio** | 🚫 Not Supported | 📅 **v1.2.0+** | Protocol schemas are ready, but underlying compute pipelines accept **Text Only** inputs. |