/**
 * AI Orchestration Console Page Module Logic
 */

// Local state configuration read from DOM root wrapper element
const rootNode = document.getElementById('console-page-root');
const coreApiUrl = rootNode ? rootNode.getAttribute('data-api-url') : '';
const initialWarningMessage = rootNode ? rootNode.getAttribute('data-warning-message') : '';

let currentStreamReader = null;
let abortController = null;
let chatHistory = [];

async function saveSamplingParams() {
    const params = {
        temperature: document.getElementById('param-temp').value,
        top_p: document.getElementById('param-topp').value,
        top_k: document.getElementById('param-topk').value,
        repeat_penalty: document.getElementById('param-rep-penalty').value,
        presence_penalty: document.getElementById('param-presence').value,
        frequency_penalty: document.getElementById('param-frequency').value,
        max_tokens: document.getElementById('param-tokens').value,
        seed: document.getElementById('param-seed').value
    };

    try {
        const response = await fetch('/api/settings/save-defaults', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(params)
        });

        if (response.ok) {
            showConsoleAlert("Parameters saved successfully!");
        } else {
            showConsoleAlert("Failed to save parameters.");
        }
    } catch (error) {
        console.error("Save error:", error);
        showConsoleAlert("Error connecting to server.");
    }
}

function showConsoleAlert(message) {
    const alertDiv = document.getElementById('console-alert');
    if (!alertDiv) return;
    document.getElementById('alert-message').innerText = message;
    alertDiv.classList.add('is-visible');
    setTimeout(hideConsoleAlert, 5000);
}

function hideConsoleAlert() {
    const alertDiv = document.getElementById('console-alert');
    if (!alertDiv) return;
    alertDiv.classList.remove('is-visible');
}

function updateStatusPulse(state) {
    const pulse = document.getElementById('status-pulse');
    if (!pulse) return;

    // Clear all potential states
    pulse.className = 'status-pulse';

    if (state === 'active') {
        pulse.classList.add('status-active');
    } else if (state === 'streaming') {
        pulse.classList.add('status-streaming');
    } else {
        pulse.classList.add('status-off');
    }
}

// Emulate original DOMContentLoaded hook cleanly from internal script load
(function initConsoleOnLoad() {
    const select = document.getElementById('console-repo-select');
    if (select && select.value) updateStatusPulse('active');

    if (initialWarningMessage && initialWarningMessage.trim() !== "") {
        showConsoleAlert(initialWarningMessage);
    }
})();

function handlePromptKeydown(e) {
    if (e.key === 'Enter' && !e.shiftKey) {
        e.preventDefault();
        executeInferenceStreaming();
    }
}

function clearChatLayout() {
    const scroller = document.getElementById('chat-scroller');
    if (scroller) scroller.innerHTML = '';

    chatHistory = [];

    const select = document.getElementById('console-repo-select');
    updateStatusPulse(select && select.value ? 'active' : 'off');
}

async function abortInferenceStreaming() {
    if (currentStreamReader) {
        try { await currentStreamReader.cancel(); } catch (err) { console.log(err); }
    }
    if (abortController) { abortController.abort(); }
    appendSystemLogToDom("Generation aborted by operator.");
    resetUiAfterGeneration();
}


/**
 * Global state requirements. Ensure these are declared at the module level.
 */
// let currentStreamReader = null;
// let abortController = null;
// let chatHistory = []; 

/**
 * Executes the streaming inference request against the active AI model.
 * Manages chat history state for sliding window context, handles UI transitions, 
 * and processes the Server-Sent Events (SSE) stream.
 */
async function executeInferenceStreaming() {
    const repoSelect = document.getElementById('console-repo-select');
    const promptInput = document.getElementById('user-prompt-input');
    const scroller = document.getElementById('chat-scroller');
    const sendBtn = document.getElementById('send-prompt-btn');
    const stopBtn = document.getElementById('stop-generation-btn');
    const sendIcon = document.getElementById('send-icon');
    const welcomeMsg = document.getElementById('welcome-message');

    if (!repoSelect || !promptInput) return;

    const targetModelId = repoSelect.value;
    const userPrompt = promptInput.value.trim();

    if (!targetModelId) {
        showConsoleAlert("Target model selection is required for inference.");
        return;
    }

    if (!userPrompt) return;

    if (welcomeMsg) {
        welcomeMsg.remove();
    }

    // Retains conversation context for sliding window evaluation
    chatHistory.push({ role: "user", content: userPrompt });

    promptInput.value = '';
    promptInput.disabled = true;

    if (sendBtn) sendBtn.disabled = true;
    if (stopBtn) stopBtn.classList.remove('display-none');
    if (sendIcon) sendIcon.className = 'las la-spinner spin-animation';

    updateStatusPulse('streaming');
    appendMessageToDom('user', userPrompt);

    const skeletonHtml = `
        <div class="ai-skeleton">
            <div class="skeleton-dot"></div>
            <div class="skeleton-dot"></div>
            <div class="skeleton-dot"></div>
        </div>
        <div class="ai-text-container hidden"></div>
        <div class="ai-metrics-panel hidden">
            <span><i class="las la-stopwatch"></i> TTFT: <strong class="metrics-ttft">-</strong></span>
            <span><i class="las la-bolt"></i> Speed: <strong class="metrics-tps">-</strong></span>
        </div>
    `.replace(/\s+/g, ' ').trim();

    const aiContainerBox = appendMessageToDom('assistant', skeletonHtml);

    let requestStartTime = null;
    let firstTokenTime = null;
    let generatedTokenCount = 0;

    const skeletonElement = aiContainerBox.querySelector('.ai-skeleton');
    const textContainerElement = aiContainerBox.querySelector('.ai-text-container');
    const metricsPanelElement = aiContainerBox.querySelector('.ai-metrics-panel');
    const ttftElement = aiContainerBox.querySelector('.metrics-ttft');
    const tpsElement = aiContainerBox.querySelector('.metrics-tps');

    marked.setOptions({
        breaks: true,
        gfm: true
    });

    abortController = new AbortController();
    let accumulatedResponse = "";

    try {
        const systemInstruction = document.getElementById('param-system')?.value || "";
        const messagesPayload = [];

        // Assembles payload prioritizing system instruction, followed by retained chat history
        if (systemInstruction.trim() !== "") {
            messagesPayload.push({ role: "system", content: systemInstruction });
        }

        messagesPayload.push(...chatHistory);

        const inferencePayload = {
            model: targetModelId,
            messages: messagesPayload,
            temperature: parseFloat(document.getElementById('param-temp')?.value || "0.7"),
            top_p: parseFloat(document.getElementById('param-topp')?.value || "0.9"),
            top_k: parseInt(document.getElementById('param-topk')?.value || "40"),
            repeat_penalty: parseFloat(document.getElementById('param-rep-penalty')?.value || "1.1"),
            presence_penalty: parseFloat(document.getElementById('param-presence')?.value || "0.0"),
            frequency_penalty: parseFloat(document.getElementById('param-frequency')?.value || "0.0"),
            max_tokens: parseInt(document.getElementById('param-tokens')?.value || "2048"),
            seed: parseInt(document.getElementById('param-seed')?.value || "-1"),
            stream: true
        };

        requestStartTime = performance.now();

        const response = await fetch(`${coreApiUrl}/v1/chat/completions`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(inferencePayload),
            signal: abortController.signal
        });

        if (!response.ok) {
            throw new Error(`API rejected generation request: ${response.statusText}`);
        }

        currentStreamReader = response.body.getReader();
        const textDecoder = new TextDecoder("utf-8");
        let streamBuffer = "";

        while (true) {
            const { value, done } = await currentStreamReader.read();
            if (done) break;

            const chunk = textDecoder.decode(value, { stream: true });
            const streamLines = chunk.split('\n');
            streamBuffer = streamLines.pop();

            for (let line of streamLines) {
                line = line.trim();
                if (!line || line === 'data: [DONE]') continue;

                if (line.startsWith('data: ')) {
                    try {
                        const parsedStreamData = JSON.parse(line.slice(6));
                        const deltaContent = parsedStreamData.choices[0]?.delta?.content || "";

                        if (deltaContent) {
                            if (!firstTokenTime) {
                                firstTokenTime = performance.now();
                                const timeToFirstToken = ((firstTokenTime - requestStartTime) / 1000).toFixed(2);

                                if (ttftElement) ttftElement.innerText = `${timeToFirstToken}s`;
                                if (skeletonElement) skeletonElement.remove();
                                if (metricsPanelElement) {
                                    metricsPanelElement.classList.remove('hidden');
                                    setTimeout(() => metricsPanelElement.classList.add('is-visible'), 10);
                                }
                            }

                            if (textContainerElement && textContainerElement.classList.contains('hidden')) {
                                textContainerElement.classList.remove('hidden');
                            }

                            accumulatedResponse += deltaContent;

                            const sanitizedMarkdown = accumulatedResponse.replace(/^(#{1,6})([^\s#])/gm, '$1 $2');
                            textContainerElement.innerHTML = marked.parse(sanitizedMarkdown);

                            generatedTokenCount++;

                            // Calculates throughput metrics dynamically
                            if (firstTokenTime) {
                                const elapsedTimeSeconds = (performance.now() - firstTokenTime) / 1000;
                                if (elapsedTimeSeconds > 0.01) {
                                    const tokensPerSecond = (generatedTokenCount / elapsedTimeSeconds).toFixed(1);
                                    if (tpsElement) tpsElement.innerText = `${tokensPerSecond} t/s`;
                                }
                            }

                            if (scroller) scroller.scrollTop = scroller.scrollHeight;
                        }
                    } catch (parseError) {
                        // Silently ignores incomplete JSON chunks inherent to SSE streaming
                    }
                }
            }
        }

        // Persists the completed assistant response for future context trimming
        if (accumulatedResponse.trim() !== "") {
            chatHistory.push({ role: "assistant", content: accumulatedResponse });
        }

    } catch (error) {
        if (error.name === 'AbortError') {
            if (accumulatedResponse.trim() !== "") {
                chatHistory.push({ role: "assistant", content: accumulatedResponse });
            }
            return;
        }

        console.error(error);
        aiContainerBox.innerHTML = `<span class="text-rose-500 font-semibold"><i class="las la-exclamation-triangle"></i> Inference Error: ${error.message}</span>`;
    } finally {
        resetUiAfterGeneration();
    }
}

function resetUiAfterGeneration() {
    const input = document.getElementById('user-prompt-input');
    const sendBtn = document.getElementById('send-prompt-btn');
    const stopBtn = document.getElementById('stop-generation-btn');
    const sendIcon = document.getElementById('send-icon');
    const select = document.getElementById('console-repo-select');

    currentStreamReader = null;
    abortController = null;

    if (input) {
        input.disabled = false;
        input.focus();
    }
    if (sendBtn) sendBtn.disabled = false;
    if (stopBtn) stopBtn.classList.add('display-none');
    if (sendIcon) sendIcon.className = 'las la-paper-plane';

    if (select) updateStatusPulse(select.value ? 'active' : 'off');
}

function appendMessageToDom(role, htmlOrText) {
    const scroller = document.getElementById('chat-scroller');
    if (!scroller) return null;

    const wrapper = document.createElement('div');

    if (role === 'user') {
        wrapper.className = 'bubble-wrapper-user';
        wrapper.innerHTML = `<div class="chat-bubble-user">${htmlOrText}</div>`;
    } else {
        wrapper.className = 'bubble-wrapper-assistant';
        wrapper.innerHTML = `<div class="chat-bubble-assistant">${htmlOrText}</div>`;
    }

    scroller.appendChild(wrapper);
    scroller.scrollTop = scroller.scrollHeight;
    return wrapper.querySelector('div');
}

function appendSystemLogToDom(text) {
    const scroller = document.getElementById('chat-scroller');
    if (!scroller) return;
    const logDiv = document.createElement('div');
    logDiv.className = 'system-log-message';
    logDiv.innerHTML = `<i class="las la-info-circle"></i> ${text}`;
    scroller.appendChild(logDiv);
    scroller.scrollTop = scroller.scrollHeight;
}

// Bind native event listeners safely instead of inline HTML tracking
const repoSelectEl = document.getElementById('console-repo-select');
if (repoSelectEl) {
    repoSelectEl.addEventListener('change', function () {
        const label = document.getElementById('current-model-label');
        if (label) label.innerText = this.value || "No active pipeline target";
        updateStatusPulse(this.value ? 'active' : 'off');
    });
}

marked.setOptions({
    breaks: true,
    gfm: true
});