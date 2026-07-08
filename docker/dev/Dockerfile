# ==========================================
# 1. NuGet Restore with Caching (BuildKit)
# ==========================================
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS dotnet-restore
WORKDIR /src

COPY ["src/InstantAIGate.API/InstantAIGate.API.csproj", "src/InstantAIGate.API/"]
COPY ["src/InstantAIGate.Admin/InstantAIGate.Admin.csproj", "src/InstantAIGate.Admin/"]
COPY ["src/InstantAIGate.Infrastructure/InstantAIGate.Infrastructure.csproj", "src/InstantAIGate.Infrastructure/"]

RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet restore "src/InstantAIGate.API/InstantAIGate.API.csproj" && \
    dotnet restore "src/InstantAIGate.Admin/InstantAIGate.Admin.csproj"

# ==========================================
# 2. Build & Publish
# ==========================================
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS dotnet-build
WORKDIR /src

COPY ["src/InstantAIGate.API/InstantAIGate.API.csproj", "src/InstantAIGate.API/"]
COPY ["src/InstantAIGate.Admin/InstantAIGate.Admin.csproj", "src/InstantAIGate.Admin/"]
COPY ["src/InstantAIGate.Infrastructure/InstantAIGate.Infrastructure.csproj", "src/InstantAIGate.Infrastructure/"]
COPY src/ src/

ARG BUILD_VERSION=1.0.7
ENV APP_VERSION=${BUILD_VERSION:-1.0.7}

RUN dotnet publish "src/InstantAIGate.API/InstantAIGate.API.csproj" \
    -c Release -o /app/publish/api -r linux-x64 --self-contained true /p:Version=${BUILD_VERSION}

RUN dotnet publish "src/InstantAIGate.Admin/InstantAIGate.Admin.csproj" \
    -c Release -o /app/publish/admin /p:Version=${BUILD_VERSION}

# ==========================================
# 3. API Runtime (CUDA + GPU)
# ==========================================
FROM nvidia/cuda:12.8.1-runtime-ubuntu22.04 AS api-runtime
WORKDIR /app

RUN apt-get update && apt-get install -y --no-install-recommends \
    libicu-dev \
    libgomp1 \
    && rm -rf /var/lib/apt/lists/*

RUN rm -f /app/*.so /app/*.so.0 2>/dev/null || true

COPY --from=dotnet-build /app/publish/api .
EXPOSE 80

# ИСПРАВЛЕНИЕ: Добавлен mkdir -p для предотвращения падения сборки (can't cd) 
# и защита симлинков (если C# код не удалит папку)
RUN mkdir -p /app/runtimes/linux-x64/cuda && cd /app/runtimes/linux-x64/cuda && \
    ln -sf libggml.so libggml.so.0 && \
    ln -sf libggml-base.so libggml-base.so.0 && \
    ln -sf libggml-cuda.so libggml-cuda.so.0 && \
    ln -sf libggml-cpu.so libggml-cpu.so.0 && \
    ln -sf libllama.so libllama.so.0

RUN mkdir -p /app/runtimes/linux-x64/cpu && cd /app/runtimes/linux-x64/cpu && \
    ln -sf libggml-base.so libggml-base.so.0 2>/dev/null || true && \
    ln -sf libggml-cpu.so libggml-cpu.so.0 2>/dev/null || true

RUN mkdir -p /app/runtimes/linux-x64/vulkan && cd /app/runtimes/linux-x64/vulkan && \
    ln -sf libggml-base.so libggml-base.so.0 2>/dev/null || true && \
    ln -sf libggml-vulkan.so libggml-vulkan.so.0 2>/dev/null || true

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