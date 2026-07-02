# InstantAIGate.Runtimes.linux-x64.Cuda

Native CUDA runtimes for linux-x64. This package provides the unmanaged, hardware-accelerated binaries required to run **InstantAIGate** middleware on Linux using NVIDIA GPUs.

InstantAIGate is a lightweight middleware providing a self-hosted, monitored foundation for local AI applications. 

## 📦 Requirements

* **Framework**: .NET 10.
* **OS**: Ubuntu 22.04 LTS (or any glibc-compatible modern Linux distribution).
* **CUDA**: Requires CUDA **12.8** compatible drivers (Minimum Host Driver Version: **570.xx+**).

## 🚀 Hardware Support Matrix

Supported NVIDIA architectures (`CMAKE_CUDA_ARCHITECTURES`) include:
* **86** — Ampere (RTX 30xx, A100, A30, A40, A10).
* **89** — Ada Lovelace (RTX 40xx, L4, L40).
* **90** — Hopper (H100, H200).
* **100** — Blackwell (B100, B200).
* **120** — Rubin (Next-gen architecture).

## ⚖️ License

This project is licensed under the **Apache License 2.0**.
Copyright (c) 2026 Instancium™.