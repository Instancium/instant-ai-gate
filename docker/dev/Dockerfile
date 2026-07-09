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
FROM nvidia/cuda:13.3.0-runtime-ubuntu24.04 AS api-runtime
WORKDIR /app

RUN apt-get update && apt-get install -y --no-install-recommends \
    libicu-dev \
    libgomp1 \
    && rm -rf /var/lib/apt/lists/*

RUN rm -f /app/*.so /app/*.so.0 2>/dev/null || true

COPY --from=dotnet-build /app/publish/api .
EXPOSE 80

RUN mkdir -p /app/runtimes/linux/cuda /app/runtimes/linux/cpu /app/runtimes/linux/vulkan
ENV LD_LIBRARY_PATH=/app/runtimes/linux/cuda:/app/runtimes/linux/cpu:/app/runtimes/linux/vulkan:/app:$LD_LIBRARY_PATH
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