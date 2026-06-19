
<p align="center">
  <img src="media/ig-logo.png" alt="InstantAIGate logo" height="180" />
  <br />
  <strong>Standardized. Secure. Instant Deployment.</strong>
  <br />
  Lightweight middleware providing a self-hosted, monitored foundation for local AI applications.
</p>

<p align="center">
  <a href="README.md"><b>Overview</b></a> │ 
  <a href="INSTALLATION.md"><b>Installation Guide</b></a> │ 
  <a href="DATASHEET.md"><b>Technical Data Sheet</b></a>
</p>

<p align="center">
  <a href="#-quick-start-60s"><img src="https://img.shields.io/badge/Docker%20Hub-Available-blue?style=flat-square&logo=docker" alt="Docker Hub"></a>
  <img src="https://img.shields.io/badge/Hardware-CPU%20%26%20GPU-flash?style=flat-square" alt="Hardware Support">
  <img src="https://img.shields.io/badge/API-OpenAI%20Compatible-orange?style=flat-square" alt="OpenAI API">
  <img src="https://img.shields.io/badge/Architecture-DDD-purple?style=flat-square" alt="DDD">
  <img src="https://img.shields.io/badge/license-Apache%202.0-green?style=flat-square" alt="License">
</p>

---
## Getting Started with Docker Compose

Deploying the entire high-performance AI gateway infrastructure is completely automated. Because this repository is public, all native hardware-acceleration drivers (CUDA, Vulkan, CPU backends) are automatically fetched, cached, and configured directly inside the multi-stage Docker build pipeline.

## 1. Clone the Repository

Clone the project repository to your local environment and navigate into the root directory:

```bash
git clone https://github.com/Instancium/instant-ai-gate.git
cd instant-ai-gate
docker compose up -d --build
```

> 💡 **What happens under the hood:**
>
> Docker will instantly spin up a secure, multi-stage compilation context, download the pre-compiled native Linux-x64 computing cores from the production release registry, and route your active hardware devices (including NVIDIA GPUs) directly to the inference core.

## 3. Access the Gateway Applications

Once the containers report an active operational status, you can immediately access the local deployment endpoints:

- **Management UI Console:** http://127.0.0.1:49153/
- **Core Processing Inference API:** http://127.0.0.1:49152/

> ⚠️ **Important Note on Asset Fetching & Layer Caching**
>
> To maximize build performance, all native hardware acceleration drivers are stored in Docker's layer cache. Changes to your C# source files inside the `src/` directory will **not** trigger a re-download of these large runtime assets.
>
> However, if you explicitly rebuild the project using the `--no-cache` flag, Docker will discard the cached layers and download the native runtime components again.

```bash
docker compose build --no-cache
docker compose up -d
```
