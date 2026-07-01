# ==========================================
# 1. NuGet Restore with Caching (BuildKit)
# ==========================================
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS dotnet-restore
WORKDIR /src

# Copy ONLY project files (for layer caching)
COPY ["src/InstantAIGate.API/InstantAIGate.API.csproj", "src/InstantAIGate.API/"]
COPY ["src/InstantAIGate.Admin/InstantAIGate.Admin.csproj", "src/InstantAIGate.Admin/"]
COPY ["src/InstantAIGate.Infrastructure/InstantAIGate.Infrastructure.csproj", "src/InstantAIGate.Infrastructure/"]

# Restore dependencies WITH CACHING via BuildKit
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet restore "src/InstantAIGate.API/InstantAIGate.API.csproj" && \
    dotnet restore "src/InstantAIGate.Admin/InstantAIGate.Admin.csproj"

# ==========================================
# 2. Build & Publish
# ==========================================
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS dotnet-build
WORKDIR /src

# Install utilities required for downloading and extracting runtime assets
RUN apt-get update && apt-get install -y curl unzip && rm -rf /var/lib/apt/lists/*

# Copy project files again (Docker caches this layer if .csproj files haven't changed)
COPY ["src/InstantAIGate.API/InstantAIGate.API.csproj", "src/InstantAIGate.API/"]
COPY ["src/InstantAIGate.Admin/InstantAIGate.Admin.csproj", "src/InstantAIGate.Admin/"]
COPY ["src/InstantAIGate.Infrastructure/InstantAIGate.Infrastructure.csproj", "src/InstantAIGate.Infrastructure/"]

# Fetch and extract the public Linux x64 hardware acceleration runtime assets
RUN curl -fSL "https://github.com/Instancium/instant-ai-gate/releases/download/v1.0.2/instant-ai-gate-runtime-v1.0.2-linux-x64.zip" -o /tmp/runtime.zip && \
    mkdir -p .runtimes && \
    unzip -q /tmp/runtime.zip -d .runtimes/ && \
    rm -rf /tmp/runtime.zip

# Now copy ALL source code
COPY src/ src/

# Define build argument for application versioning (defaults to 1.0.2)
ARG BUILD_VERSION=1.0.5
ENV APP_VERSION=${BUILD_VERSION:-1.0.5}

# Publish API as self-contained (with GitVersion bypass flag added)
RUN dotnet publish "src/InstantAIGate.API/InstantAIGate.API.csproj" \
    -c Release -o /app/publish/api -r linux-x64 --self-contained true /p:Version=${BUILD_VERSION}

# Publish Admin as framework-dependent (using aspnet:10.0 at runtime)
RUN dotnet publish "src/InstantAIGate.Admin/InstantAIGate.Admin.csproj" \
    -c Release -o /app/publish/admin /p:Version=${BUILD_VERSION}

# ==========================================
# 3. API Runtime (CUDA + GPU)
# ==========================================
FROM nvidia/cuda:12.8.1-runtime-ubuntu22.04 AS api-runtime
WORKDIR /app

# Install system dependencies (SINGLE RUN to optimize layers)
RUN apt-get update && apt-get install -y --no-install-recommends \
    libicu-dev \
    libgomp1 \
    && rm -rf /var/lib/apt/lists/*

# This is needed so the loader doesn't try to pick up broken libraries from /app
RUN rm -f /app/*.so /app/*.so.0 2>/dev/null || true

# Copy the published API application
COPY --from=dotnet-build /app/publish/api .
EXPOSE 80

# Copy the automated downloaded native libraries from the build container
COPY --from=dotnet-build /src/.runtimes/linux-x64/ /app/runtimes/linux-x64/

# libllama.so looks for libggml.so.0, but we only have libggml.so
RUN cd /app/runtimes/linux-x64/cuda && \
    ln -sf libggml.so libggml.so.0 && \
    ln -sf libggml-base.so libggml-base.so.0 && \
    ln -sf libggml-cuda.so libggml-cuda.so.0 && \
    ln -sf libggml-cpu.so libggml-cpu.so.0 && \
    ln -sf libllama.so libllama.so.0

RUN cd /app/runtimes/linux-x64/cpu && \
    ln -sf libggml-base.so libggml-base.so.0 2>/dev/null || true && \
    ln -sf libggml-cpu.so libggml-cpu.so.0 2>/dev/null || true

RUN cd /app/runtimes/linux-x64/vulkan && \
    ln -sf libggml-base.so libggml-base.so.0 2>/dev/null || true && \
    ln -sf libggml-vulkan.so libggml-vulkan.so.0 2>/dev/null || true

# This ensures the loader will look for libraries in the right places
ENV LD_LIBRARY_PATH=/app/runtimes/linux-x64/cuda:/app/runtimes/linux-x64/cpu:/app/runtimes/linux-x64/vulkan:/app:$LD_LIBRARY_PATH
ENV INSTANTAIGATE_CONFIG_PATH=/app/gateway/config

RUN chmod +x ./InstantAIGate.API
ENTRYPOINT ["./InstantAIGate.API"]

# ==========================================
# 4. Admin Runtime (lightweight image)
# ==========================================
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS admin-runtime
WORKDIR /app
COPY --from=dotnet-build /app/publish/admin .
ENV INSTANTAIGATE_CONFIG_PATH=/app/gateway/config
EXPOSE 80
ENTRYPOINT ["dotnet", "InstantAIGate.Admin.dll"]