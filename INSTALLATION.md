
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
  <a href="#-quick-start-60s"><img src="https://img.shields.io/badge/GHCR-Available-blue?style=flat-square&logo=github" alt="GitHub Container Registry"></a>
  <img src="https://img.shields.io/badge/Hardware-CPU%20%26%20GPU-flash?style=flat-square" alt="Hardware Support">
  <img src="https://img.shields.io/badge/API-OpenAI%20Compatible-orange?style=flat-square" alt="OpenAI API">
  <img src="https://img.shields.io/badge/Architecture-DDD-purple?style=flat-square" alt="DDD">
  <img src="https://img.shields.io/badge/license-Apache%202.0-green?style=flat-square" alt="License">
</p>

## Easy Start: Running with Docker Compose

Deploying the entire high-performance AI gateway infrastructure is now completely automated and takes just seconds. 

We provide pre-built, highly-optimized Docker images hosted on the GitHub Container Registry (GHCR). All native hardware-acceleration drivers (CUDA, Vulkan, CPU backends) are **already baked into these images**, meaning zero compilation time and no massive downloads during setup.

### 1. Clone the Repository

Clone the project repository to your local environment and navigate into the root directory:

```bash
git clone https://github.com/Instancium/instant-ai-gate.git
cd instant-ai-gate
```

### 2. Launch the Infrastructure
Start the gateway in detached mode using Docker Compose:
```bash
docker compose up -d
```
> 💡 **What happens under the hood:**
> Docker will instantly pull the pre-compiled native Linux-x64 computing cores from GHCR. It will completely bypass any compilation steps and automatically route your active hardware devices (including NVIDIA GPUs) directly to the inference core for maximum performance.

### 3. Access the Gateway Applications
Once the containers report an active operational status, you can immediately access the local deployment endpoints:

- **Management UI Console:** [http://127.0.0.1:49153/](http://127.0.0.1:49153/)
- **Core Processing Inference API:** [http://127.0.0.1:49152/](http://127.0.0.1:49152/)



> 🔄 **Updating to the latest version**
> Because everything is pre-built, keeping your gateway up to date is trivial. To fetch the newest features and performance improvements without losing your downloaded models, simply run:

```bash
docker compose pull
docker compose up -d
```
