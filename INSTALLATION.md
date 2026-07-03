
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


# 🐋 Easy Start: Running with Docker Compose

Deploying the entire high-performance AI gateway infrastructure is completely automated and takes just seconds. We provide pre-built, highly-optimized Docker images hosted on the GitHub Container Registry (GHCR) with native hardware-acceleration drivers (CUDA, Vulkan) already baked in.

## 🛠 Prerequisites

Before starting, make sure you have Docker installed on your system:
* **Windows / macOS / Linux:** Download and install [Docker Desktop](https://www.docker.com/products/docker-desktop/).
* **NVIDIA GPU Users (Linux):** Ensure you have the [NVIDIA Container Toolkit](https://docs.nvidia.com/datacenter/cloud-native/container-toolkit/latest/install-guide.html) installed so Docker can access your graphics card.

---

## 🚀 Deployment Steps

### 1. Prepare Workspace
Create a new directory for the project and navigate into it:
```bash
mkdir instant-ai-gate && cd instant-ai-gate
```

### 2. Create Configuration File
Create a file named `docker-compose.yml` and paste the following production configuration inside it:

```yaml
# ==========================================
# PRODUCTION COMPOSE FILE (For End-Users)
# Uses pre-built images from GitHub Container Registry
# ==========================================
services:
  api:
    image: ghcr.io/instancium/instant-ai-gate-api:latest
    container_name: instant-ai-gate-api
    restart: unless-stopped
    ports:
      - "127.0.0.1:49152:80"
    volumes:
      - ./storage/models:/app/storage/models
      - ./gateway/config:/app/gateway/config
    environment:
      - INSTANTAIGATE_CONFIG_PATH=/app/gateway/config
      - Storage__RootPath=/app/storage/models
      - CorsSettings__AllowedOrigins__0=http://localhost:49153
      - CorsSettings__AllowedOrigins__1=http://127.0.0.1:49153
      - Logging__LogLevel__Default=Warning
      - ASPNETCORE_URLS=http://+:80
      - ASPNETCORE_ENVIRONMENT=Production
      - NVIDIA_VISIBLE_DEVICES=all
    networks:
      - app-network
    # GPU Support Configuration
    deploy:
      resources:
        reservations:
          devices:
            - driver: nvidia
              count: all
              capabilities: [gpu, compute, utility]

  admin:
    image: ghcr.io/instancium/instant-ai-gate-admin:latest
    container_name: instant-ai-gate-admin
    restart: unless-stopped
    ports:
      - "127.0.0.1:49153:80"
    volumes:
      - ./gateway/config:/app/gateway/config
    depends_on:
      - api
    environment:
      - INSTANTAIGATE_CONFIG_PATH=/app/gateway/config
      - ASPNETCORE_ENVIRONMENT=Production
      - ASPNETCORE_URLS=http://+:80
      - Logging__LogLevel__Default=Warning
      - APIClientOptions__BaseUrl=http://api:80
      - APIClientOptions__PublicUrl=http://127.0.0.1:49152
    networks:
      - app-network

networks:
  app-network: 
    driver: bridge
```

### 3. Launch the Infrastructure
Start the gateway in detached mode. Docker will instantly pull the pre-compiled images from GHCR and automatically route your active hardware devices (including NVIDIA GPUs) to the core:
```bash
docker compose up -d
```

⚠️ ATTENTION: FIRST LAUNCH INITIALIZATION > On the very first startup, the API service will automatically extract and initialize heavy native hardware-acceleration drivers (e.g., CUDA libraries) into your mounted volume. This one-time process expands approximately 500MB of data and can take 1 to 3 minutes depending on your disk speed.

During this phase, the Management UI will display an "Updating drivers" status indicator. Please be patient and do not restart the containers until the extraction completes and the status changes to "Ready".

  <img src="media/status-ready.png" alt="InstantAIGate logo" />

---

## 🌐 Accessing the Gateway

Once the containers report an active operational status, you can immediately access the local deployment endpoints:

* **Management UI Console:** [http://127.0.0.1:49153/](http://127.0.0.1:49153/)
* **Core Processing Inference API:** [http://127.0.0.1:49152/](http://127.0.0.1:49152/)

---

## 🔄 Updating to the Latest Version

Because everything is pre-built, keeping your gateway up to date is trivial. To fetch the newest features and performance improvements without losing your downloaded models or settings, simply run:

```bash
docker compose pull
docker compose up -d
```
