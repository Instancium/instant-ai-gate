# Third-Party Notices & Licenses

This project uses third-party software components. Below is a comprehensive list of these components, their licenses, and where to find their original license terms.

## 1. Native Inference Runtime

| Component | License | Source / Notes |
| --- | --- | --- |
| [llama.cpp](https://github.com/ggerganov/llama.cpp) | MIT | Core C++ inference engine. Compiled as native shared libraries (`llama.dll` / `libllama.so`) and consumed via P/Invoke from `InstantAIGate.Native`. |
| [ggml](https://github.com/ggerganov/ggml) | MIT | Tensor library and hardware backends. Provides `ggml-base`, `ggml-cpu`, `ggml-vulkan`. |
| [mtmd](https://github.com/ggerganov/llama.cpp/tree/master/tools/mtmd) | MIT | Multi-modal (vision / audio) decoder built on top of llama.cpp. Used for Qwen-VL projector evaluation. |

## 2. Supported Model Weights

| Component | License | Source / Notes |
| --- | --- | --- |
| [Qwen](https://huggingface.co/Qwen) (Qwen3-VL) | Apache 2.0 | InstantAIGate is purpose-built and deeply optimized for the Qwen family of models. Qwen weights are downloaded directly from Hugging Face or ModelScope under the permissive Apache 2.0 license. We gratefully acknowledge the Qwen team's mission to democratize artificial intelligence. |

## 3. .NET Ecosystem Libraries

| Component | License | Usage |
| --- | --- | --- |
| Microsoft.AspNetCore.* | MIT | ASP.NET Core runtime, SignalR server/client, Health Checks, Authorization, Mvc.Testing |
| Microsoft.Extensions.* | MIT | Logging, Options, Hosting, Http, DependencyInjection, Configuration, TimeProvider, TimeProvider.Testing |
| Microsoft.Extensions.Hosting.WindowsServices | MIT | Windows Service integration for the gateway runtime |

## 4. Client-Side Libraries (CLI)

| Component | License | Source / Notes |
| --- | --- | --- |
| [Spectre.Console](https://github.com/spectreconsole/spectre.console) | MIT | Rich terminal UI framework used by the reference CLI client (`InstantAIGate.Cli`). |

## 5. Testing Libraries

| Component | License | Source / Notes |
| --- | --- | --- |
| [xUnit](https://github.com/xunit/xunit) | Apache 2.0 | Primary unit and integration testing framework. |
| [Moq](https://github.com/moq/moq4) | BSD 3-Clause | Mocking framework for unit tests. |
| [coverlet](https://github.com/coverlet-oss/coverlet) | MIT | Cross-platform code coverage collector. |

## License Texts and Acknowledgements

### .NET Foundation & Microsoft Libraries
The .NET runtime, ASP.NET Core, and `Microsoft.Extensions.*` libraries are licensed under the MIT License by the .NET Foundation.

### llama.cpp, ggml & mtmd
Copyright (c) 2023–2026 Georgi Gerganov and contributors. Licensed under the MIT License.

### Qwen Model Family
Copyright (c) Alibaba Cloud. The Qwen model weights used by InstantAIGate are released under the Apache License 2.0.

### Spectre.Console, xUnit, Moq, coverlet
These libraries are distributed under their respective open-source licenses (MIT, Apache 2.0, BSD 3-Clause).