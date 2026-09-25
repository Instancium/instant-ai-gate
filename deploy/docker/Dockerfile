# Stage 1: Build Phase
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and project files
COPY . .

# Publish the application as a self-contained Linux executable.
# The "-r linux-x64" flag guarantees that the CreateLinuxSymlinks MSBuild target
# is executed, properly projecting libggml.so and libllama.so symlinks.
RUN dotnet publish src/InstantAIGate.Server/InstantAIGate.Server.csproj \
    -c Release \
    -r linux-x64 \
    --self-contained true \
    -o /app/publish \
    /p:PublishSingleFile=false

# Stage 2: Runtime Phase
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-bookworm-slim AS final
WORKDIR /app

# Install native runtime dependencies required by llama.cpp (OpenMP and Vulkan ICD loader).
# Hardware drivers (NVIDIA/AMD) are explicitly EXCLUDED to strictly rely on host passthrough.
RUN apt-get update && apt-get install -y --no-install-recommends \
    libgomp1 \
    libvulkan1 \
    && rm -rf /var/lib/apt/lists/*

# Copy the compiled binaries and generated symlinks from the build stage
COPY --from=build /app/publish .

# Expose Public (5000) and Admin (5001) endpoints
EXPOSE 5000 5001

# Enforce environment configuration
ENV Kestrel__Endpoints__PublicEndpoint__Url=http://0.0.0.0:5000
ENV Kestrel__Endpoints__AdminEndpoint__Url=http://0.0.0.0:5001
ENV InstantAIGate__Storage__ModelsDirectory=/app/models

# Define mount point for persistent zero-duplication model storage
VOLUME /app/models

ENTRYPOINT ["./InstantAIGate.Server"]