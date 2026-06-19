
## Getting Started with Docker Compose

To spin up the infrastructure seamlessly, follow these setup steps. Because this repository isolates heavy unmanaged hardware runtimes (CUDA, Vulkan, CPU backends) to keep the source tree lightweight, you must download and place the runtime drivers manually inside the project directory before launching Docker.

> ⚠️ **Note on Automation**
>
> While this repository remains private, automated downloading inside Docker requires strict authentication tokens. A seamless, automatic download sequence directly inside the Dockerfile will be deployed once the project transitions to a public scope.


### 1. Clone the Repository

First, clone the project repository to your local environment and navigate into the root directory:

```bash
git clone https://github.com/Instancium/instant-ai-gate.git
cd instant-ai-gate
```

### 2. Setup Local Runtime Drivers

1. Navigate to the repository **Releases** page in your browser.
2. Download the hardware drivers archive:

   ```
   instant-ai-gate-runtime-v1.0.0.zip
   ```

3. Inside the newly cloned `instant-ai-gate` directory, create a new folder named `.runtimes`.
4. Extract the contents of the downloaded ZIP archive directly into that `.runtimes` folder.

Verify that your local directory tree looks exactly like this before proceeding:

```text
instant-ai-gate/
├── Dockerfile
├── docker-compose.yml
├── src/
└── .runtimes/
    └── linux-x64/
        ├── cpu/
        ├── cuda/
        └── vulkan/
```

### 3. Build and Launch Infrastructure

Once the `.runtimes` directory tree is fully populated, execute the following command to build and start the containers:

```bash
docker-compose up -d --build
```

### 4. Access the Gateway Applications

Once the multi-stage containers successfully build and report an active status, you can immediately access the endpoints:

- **Admin Panel Web UI:** http://127.0.0.1:49153/
- **AI Core Gateway API:** http://127.0.0.1:49152/