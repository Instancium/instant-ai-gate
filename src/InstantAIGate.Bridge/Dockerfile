FROM nvcr.io/nvidia/trtllm/tensorrt-llm-devel:latest AS builder

WORKDIR /app

COPY CMakeLists.txt .
COPY src/ ./src/

RUN mkdir build && cd build && \
    cmake .. -DCMAKE_BUILD_TYPE=Release && \
    cmake --build . --config Release

FROM scratch AS export-stage
COPY --from=builder /app/build/Release/libInstantAIGateBridge.so /