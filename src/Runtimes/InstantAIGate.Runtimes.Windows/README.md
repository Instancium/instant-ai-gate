# InstantAIGate.Runtimes.Windows

## Overview
**Package ID:** `InstantAIGate.Runtimes.Windows`
**Version:** 1.0.10
**Author:** Instancium
**Target Framework:** `netstandard2.0`

This package provides native Windows runtimes (CPU/CUDA) for **llama.cpp**, embedded as a compressed `windows-x64.7z` archive. It is designed to act as a drop-in execution backend for AI applications requiring hardware-accelerated inference on Windows systems.

## Base Software Specifications
The native libraries included in this package are strictly derived from the `llama.cpp` upstream repository.
* **Upstream Project:** llama.cpp
* **Source Date:** June 14, 2026
* **Commit Hash:** `6e14286`
* **Build Target:** x64 (Release)

## Hardware & Architecture Support
This runtime is compiled as a "Fat Binary" with universal GPU support via CUDA. 
* **CUDA Toolkit Version:** CUDA 13.3
* **CUDA Runtime Library:** Static

### Supported NVIDIA GPU Architectures
The binary includes compiled kernels for the following CUDA architectures (SM versions):
| SM Version | Code Name / Generation (Approx.) | Note |
| :--- | :--- | :--- |
| **86** | Ampere | RTX 30-series, A-series |
| **89** | Ada Lovelace | RTX 40-series |
| **90** | Hopper | H100 / H200 |
| **100** | Blackwell | Server/Datacenter (B100) |
| **120** | Blackwell | Consumer (RTX 50-series) |

## Embedded Artifacts
The embedded `windows-x64.7z` archive contains the following compiled dynamic link libraries (DLLs):
* `llama.dll`
* `ggml.dll`
* `ggml-base.dll`
* `ggml-cpu.dll`
* `ggml-cuda.dll`

## Software & Driver Requirements

**⚠️ CRITICAL REQUIREMENT:** The host system's NVIDIA drivers MUST be updated to support a minimum of CUDA 13.3.0. Older drivers will cause initialization failures.

## ⚖️ License

This package is a component of the [InstantAIGate](https://github.com/Instancium/instant-ai-gate) project.  
It is licensed under the **Apache License 2.0**.  
Copyright (c) 2026 Instancium™.


### Third-Party Licenses

This package includes compiled binaries of [llama.cpp](https://github.com/ggerganov/llama.cpp), which is licensed under the **MIT License**. 

**llama.cpp Copyright Notice:**
> MIT License
> 
> Copyright (c) 2023-2024 The ggml authors
> 
> Permission is hereby granted, free of charge, to any person obtaining a copy
> of this software and associated documentation files (the "Software"), to deal
> in the Software without restriction, including without limitation the rights
> to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
> copies of the Software, and to permit persons to whom the Software is
> furnished to do so, subject to the following conditions:
> 
> The above copyright notice and this permission notice shall be included in all
> copies or substantial portions of the Software.
> 
> THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
> IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
> FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
> AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
> LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
> OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
> SOFTWARE.