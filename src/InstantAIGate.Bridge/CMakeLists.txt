cmake_minimum_required(VERSION 3.10)
project(InstantAIGate.Bridge VERSION 1.0.0 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

set(CMAKE_RUNTIME_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/bin)
set(CMAKE_LIBRARY_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/bin)
set(CMAKE_ARCHIVE_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/bin)

add_library(InstantAIGateBridge SHARED
    src/bridge.cpp
)

target_compile_definitions(InstantAIGateBridge PRIVATE BRIDGE_EXPORTS)
set_target_properties(InstantAIGateBridge PROPERTIES OUTPUT_NAME "InstantAIGate.Bridge")