# Stage 1: Build Phase
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and project files
COPY . .

# Publish the application as a self-contained Linux executable.
RUN dotnet publish src/InstantAIGate.Server/InstantAIGate.Server.csproj \
    -c Release \
    -r linux-x64 \
    --self-contained true \
    -o /app/publish \
    /p:PublishSingleFile=false

# Stage 2: Runtime Phase
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0 AS final
WORKDIR /app

# Install native runtime dependencies required by llama.cpp (OpenMP and Vulkan ICD loader)
RUN apt-get update && apt-get install -y --no-install-recommends \
    libgomp1 \
    libvulkan1 \
    && rm -rf /var/lib/apt/lists/*

# Copy the compiled binaries and native runtimes from the build stage
COPY --from=build /app/publish .

# Expose single gateway port
EXPOSE 5000

# Bind Kestrel directly to port 5000
ENV ASPNETCORE_HTTP_PORTS=5000
ENV InstantAIGate__Storage__ModelsDirectory=/app/models

# Define mount point for persistent model storage
VOLUME /app/models

ENTRYPOINT ["./InstantAIGate.Server"]