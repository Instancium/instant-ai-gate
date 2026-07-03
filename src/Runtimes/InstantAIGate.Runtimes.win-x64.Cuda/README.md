# InstantAIGate.Runtimes.win-x64.Cuda

Native CUDA runtimes for win-x64. This package provides the unmanaged, hardware-accelerated binaries required to run **InstantAIGate** middleware on Windows using NVIDIA GPUs.

InstantAIGate is a lightweight middleware providing a self-hosted, monitored foundation for local AI applications. 

## 📦 Requirements

* **Framework**: .NET 10.
* **OS**: Windows Server 2022 / Windows 10 & 11 (64-bit).
* **CUDA**: Official NVIDIA Display Driver with CUDA **12.8** support.

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