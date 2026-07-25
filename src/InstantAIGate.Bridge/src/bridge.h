#pragma once

#include <cstdint>

#if defined(_MSC_VER)
#define BRIDGE_API __declspec(dllexport)
#elif defined(__GNUC__)
#define BRIDGE_API __attribute__((visibility("default")))
#else
#define BRIDGE_API
#endif

extern "C" {

    typedef struct ExecutorHandle ExecutorHandle;

    struct InferenceRequest {
        const char* requestId;
        const int32_t* tokens;
        int32_t tokenCount;
    };

    struct InferenceResponse {
        const char* requestId;
        int32_t* generatedTokens;
        int32_t generatedTokenCount;
        bool isCompleted;
    };

    BRIDGE_API ExecutorHandle* Bridge_InitializeExecutor();
    BRIDGE_API bool Bridge_EnqueueTask(ExecutorHandle* handle, const InferenceRequest* request);
    BRIDGE_API InferenceResponse* Bridge_AwaitResponses(ExecutorHandle* handle);
    BRIDGE_API void Bridge_FreeMemory(ExecutorHandle* handle);
    BRIDGE_API void Bridge_FreeResponse(InferenceResponse* response);
}