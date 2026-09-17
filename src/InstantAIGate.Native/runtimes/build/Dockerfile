FROM ubuntu:24.04 AS vulkan-build

# Install build dependencies
RUN apt-get update && apt-get install -y --no-install-recommends \
    ca-certificates build-essential cmake git \
    libvulkan-dev vulkan-utility-libraries-dev \
    glslc glslang-tools spirv-tools libshaderc-dev \
    && rm -rf /var/lib/apt/lists/*

# Clone and build llama.cpp
RUN git clone https://github.com/ggerganov/llama.cpp /app/llama.cpp && \
    cd /app/llama.cpp && \
    git checkout v0.4.0 && \
    mkdir build && cd build && \
    cmake -DGGML_VULKAN=1 \
          -DLLAMA_BUILD_EXAMPLES=OFF \
          -DLLAMA_BUILD_TESTS=OFF \
          -DLLAMA_BUILD_SERVER=ON \
          -DLLAMA_BUILD_TOOLS=ON \
          -DLLAMA_BUILD_CLI=ON \
          -DCMAKE_BUILD_TYPE=Release .. && \
    make -j12

# Pack artifacts into a tarball to preserve symlinks perfectly
RUN mkdir -p /artifacts && \
    cp -rv /app/llama.cpp/build/bin/* /artifacts/ && \
    cd /artifacts && \
    tar -cvf linux-x64-artifacts.tar .