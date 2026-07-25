#include "bridge.h"
#include <queue>
#include <mutex>
#include <condition_variable>
#include <cstring>

struct ExecutorHandle {
    std::mutex queueMutex;
    std::condition_variable condition;
    std::queue<InferenceResponse*> responseQueue;
    bool isRunning;

    ExecutorHandle() : isRunning(true) {}
};

extern "C" {

    BRIDGE_API ExecutorHandle* Bridge_InitializeExecutor() {
        return new ExecutorHandle();
    }

    BRIDGE_API bool Bridge_EnqueueTask(ExecutorHandle* handle, const InferenceRequest* request) {
        if (!handle || !request) {
            return false;
        }

        InferenceResponse* response = new InferenceResponse();

        size_t idLength = std::strlen(request->requestId) + 1;
        char* idCopy = new char[idLength];
        std::strncpy(idCopy, request->requestId, idLength);
        response->requestId = idCopy;

        response->generatedTokenCount = 1;
        response->generatedTokens = new int32_t[1];

        if (request->tokenCount > 0) {
            response->generatedTokens[0] = request->tokens[0];
        }
        else {
            response->generatedTokens[0] = 0;
        }

        response->isCompleted = true;

        std::lock_guard<std::mutex> lock(handle->queueMutex);
        handle->responseQueue.push(response);
        handle->condition.notify_one();

        return true;
    }

    BRIDGE_API InferenceResponse* Bridge_AwaitResponses(ExecutorHandle* handle) {
        if (!handle) {
            return nullptr;
        }

        std::unique_lock<std::mutex> lock(handle->queueMutex);
        handle->condition.wait(lock, [handle] {
            return !handle->responseQueue.empty() || !handle->isRunning;
            });

        if (!handle->isRunning && handle->responseQueue.empty()) {
            return nullptr;
        }

        InferenceResponse* response = handle->responseQueue.front();
        handle->responseQueue.pop();

        return response;
    }

    BRIDGE_API void Bridge_FreeMemory(ExecutorHandle* handle) {
        if (!handle) {
            return;
        }

        {
            std::lock_guard<std::mutex> lock(handle->queueMutex);
            handle->isRunning = false;

            while (!handle->responseQueue.empty()) {
                InferenceResponse* response = handle->responseQueue.front();
                handle->responseQueue.pop();
                Bridge_FreeResponse(response);
            }
        }

        handle->condition.notify_all();
        delete handle;
    }

    BRIDGE_API void Bridge_FreeResponse(InferenceResponse* response) {
        if (!response) {
            return;
        }

        if (response->requestId) {
            delete[] response->requestId;
        }

        if (response->generatedTokens) {
            delete[] response->generatedTokens;
        }

        delete response;
    }
}