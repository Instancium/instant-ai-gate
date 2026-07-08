# InstantAIGate.Runtimes.Linux

## Overview
**Package ID:** `InstantAIGate.Runtimes.Linux`
**Version:** 1.0.10
**Author:** Instancium
**Target Framework:** `netstandard2.0`

This package provides native Linux runtimes (CPU/CUDA) for **llama.cpp**, embedded as a compressed `linux-x64.7z` archive. It acts as the core inference engine for Linux environments, enabling hardware-accelerated execution of LLMs.

## Base Software Specifications
The shared objects (`.so`) provided in this package are derived from the official `llama.cpp` repository.
* **Upstream Project:** llama.cpp
* **Source Date:** June 14, 2026
* **Commit Hash:** `6e14286`
* **Compiler:** g++-12
* **OS Environment:** Ubuntu 22.04 (Base)

## Hardware & Architecture Support
The Linux runtime provides universal GPU support built against the latest CUDA toolkit, linked statically to ensure broad compatibility across host systems.
* **CUDA Toolkit Version:** CUDA 13.3.0
* **CUDA Runtime Library:** Static

### Supported NVIDIA GPU Architectures
The binary natively targets and includes execution cores for the following architectures:
| SM Version | Code Name / Generation (Approx.) | Note |
| :--- | :--- | :--- |
| **86** | Ampere | RTX 30-series, A-series |
| **89** | Ada Lovelace | RTX 40-series |
| **90** | Hopper | H100 / H200 |
| **100** | Blackwell | Server/Datacenter (B100) |
| **120** | Blackwell | Consumer (RTX 50-series) |

## Software & Driver Requirements

**⚠️ CRITICAL REQUIREMENT:** The host system's NVIDIA drivers MUST be updated to support a minimum of CUDA 13.3.0. Older drivers will cause initialization failures.

## Embedded Artifacts
The embedded `linux-x64.7z` archive contains the following compiled shared objects (`*.so` files):
* `libllama.so`
* `libggml-base.so`
* `libggml-cpu.so`
* `libggml-cuda.so`


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