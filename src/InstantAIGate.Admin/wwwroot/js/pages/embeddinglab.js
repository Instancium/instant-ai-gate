document.addEventListener('DOMContentLoaded', async () => {
    // --- DOM Elements ---
    const modelSelect = document.getElementById('model-select');
    const analyzeBtn = document.getElementById('analyze-btn');
    const embeddingConfigEl = document.getElementById('embedding-config');
    const apiUrl = window.AppConfig?.apiUrl || embeddingConfigEl?.dataset?.apiUrl;

    const selectWrapper = document.getElementById('model-select-wrapper');
    const selectTrigger = document.getElementById('custom-select-trigger');
    const customOptionsContainer = document.getElementById('custom-options');

    // Alert UI
    const alertDiv = document.getElementById('console-alert');
    const alertMessage = document.getElementById('alert-message');
    const alertIcon = document.getElementById('alert-icon');
    const closeAlertBtn = document.getElementById('close-alert-btn');
    let alertTimeout = null;

    // Metadata UI
    const metaDimensions = document.getElementById('meta-dimensions');
    const metaTokens = document.getElementById('meta-tokens');
    const metaLatency = document.getElementById('meta-latency');

    if (analyzeBtn) analyzeBtn.disabled = true;

    // --- Dropdown Logic ---
    if (selectTrigger && selectWrapper) {
        selectTrigger.addEventListener('click', (e) => {
            e.stopPropagation();
            selectWrapper.classList.toggle('is-open');
        });
    }

    document.addEventListener('click', () => {
        if (selectWrapper) selectWrapper.classList.remove('is-open');
    });

    // --- Alert System ---
    function showAlert(message, type = 'error') {
        if (!alertDiv || !alertMessage) return;

        if (alertTimeout) clearTimeout(alertTimeout);
        alertMessage.innerText = message;
        alertDiv.classList.remove('is-error', 'is-warning');

        if (type === 'warning') {
            alertDiv.classList.add('is-warning');
            if (alertIcon) alertIcon.className = 'bi bi-exclamation-triangle';
        } else {
            alertDiv.classList.add('is-error');
            if (alertIcon) alertIcon.className = 'bi bi-exclamation-circle';
        }

        alertDiv.classList.add('is-visible');
        alertTimeout = setTimeout(hideAlert, 5000);
    }

    function hideAlert() {
        if (!alertDiv) return;
        alertDiv.classList.remove('is-visible');
    }

    if (closeAlertBtn) {
        closeAlertBtn.addEventListener('click', hideAlert);
    }

    // --- Load Models ---
    if (apiUrl) {
        try {
            const response = await fetch(`${apiUrl}/v1/models`);
            const data = await response.json();

            if (data.data && Array.isArray(data.data) && data.data.length > 0) {
                if (customOptionsContainer) customOptionsContainer.innerHTML = '';
                if (modelSelect) modelSelect.innerHTML = '<option value="">-- Select Active Pipeline --</option>';

                data.data.forEach(model => {
                    if (modelSelect) {
                        const opt = document.createElement('option');
                        opt.value = model.id;
                        opt.textContent = model.id;
                        modelSelect.appendChild(opt);
                    }

                    if (customOptionsContainer) {
                        const divOpt = document.createElement('div');
                        divOpt.className = 'custom-option';
                        divOpt.dataset.value = model.id;
                        divOpt.textContent = model.id;

                        divOpt.addEventListener('click', function (e) {
                            e.stopPropagation();
                            if (selectTrigger) selectTrigger.querySelector('span').textContent = this.textContent;
                            if (modelSelect) modelSelect.value = this.dataset.value;

                            customOptionsContainer.querySelectorAll('.custom-option').forEach(el => el.classList.remove('is-selected'));
                            this.classList.add('is-selected');
                            selectWrapper.classList.remove('is-open');
                        });

                        customOptionsContainer.appendChild(divOpt);
                    }
                });

                if (analyzeBtn) analyzeBtn.disabled = false;
            } else {
                if (selectTrigger) selectTrigger.querySelector('span').textContent = "⚠️ No active models found";
                if (analyzeBtn) {
                    analyzeBtn.innerText = "No models available";
                    analyzeBtn.disabled = true;
                }
                showAlert("No active embedding models found on the server.", "warning");
            }
        } catch (err) {
            console.error("Failed to load models:", err);
            if (selectTrigger) selectTrigger.querySelector('span').textContent = "❌ API Connection Failed";
            showAlert("Failed to connect to the API server to load models.", "error");
        }
    }

    // --- Analysis Action ---
    if (analyzeBtn) {
        analyzeBtn.addEventListener('click', async () => {
            const text1 = document.getElementById('text1').value.trim();
            const text2 = document.getElementById('text2').value.trim();
            const selectedModel = modelSelect ? modelSelect.value : '';

            const gaugeFill = document.getElementById('gauge-fill');
            const similarityText = document.getElementById('similarity-score');

            if (!selectedModel) {
                showAlert("Please select an active pipeline model first.", "warning");
                return;
            }
            if (!text1 || !text2) {
                showAlert("Please enter text in both fields to compare.", "warning");
                return;
            }

            try {
                analyzeBtn.disabled = true;
                analyzeBtn.innerText = "Analyzing...";
                if (gaugeFill) gaugeFill.style.width = "0%";
                if (similarityText) similarityText.innerText = "0%";

                // Measure request latency
                const startTime = performance.now();

                // Send both texts in a single request as per OpenAI spec
                const responseData = await getEmbeddingsBatch([text1, text2], selectedModel);

                const latencyMs = performance.now() - startTime;

                if (!responseData.data || responseData.data.length < 2) {
                    throw new Error("Invalid response structure from server.");
                }

                const vec1 = responseData.data[0].embedding;
                const vec2 = responseData.data[1].embedding;

                // Update Metadata UI
                if (metaDimensions) metaDimensions.innerText = vec1.length;
                if (metaTokens) {
                    metaTokens.innerText = responseData.usage?.total_tokens || responseData.usage?.totalTokens || "N/A";
                }
                if (metaLatency) metaLatency.innerText = `${Math.round(latencyMs)} ms`;

                // Calculate and update similarity
                const similarity = calculateCosineSimilarity(vec1, vec2);

                // Normalize cosine similarity (-1 to 1) into a 0 to 100 percentage
                const normalizedSim = Math.max(-1, Math.min(1, similarity));
                const percentage = Math.round(((normalizedSim + 1) / 2) * 100);

                if (similarityText) similarityText.innerText = `${percentage}%`;
                if (gaugeFill) {
                    gaugeFill.style.width = `${percentage}%`;
                    let color = percentage < 40 ? "#ef4444" : (percentage < 70 ? "#f59e0b" : "#10b981");
                    gaugeFill.style.background = `linear-gradient(to right, ${color}, #a7f3d0)`;
                }

                updateChart(vec1, vec2);

            } catch (err) {
                console.error("Analysis error:", err);
                showAlert("Error during embedding generation. Check connection or model availability.", "error");
            } finally {
                analyzeBtn.disabled = false;
                analyzeBtn.innerText = "Analyze & Compare";
            }
        });
    }

    // --- Helper Functions ---
    function calculateCosineSimilarity(vecA, vecB) {
        if (vecA.length !== vecB.length || vecA.length === 0) return 0;
        let dotProduct = 0, normA = 0, normB = 0;
        for (let i = 0; i < vecA.length; i++) {
            dotProduct += vecA[i] * vecB[i];
            normA += vecA[i] * vecA[i];
            normB += vecB[i] * vecB[i];
        }
        if (normA === 0 || normB === 0) return 0;
        return dotProduct / (Math.sqrt(normA) * Math.sqrt(normB));
    }

    // Bulk processing function (1 request = 2 embeddings)
    async function getEmbeddingsBatch(textsArray, model) {
        const baseUrl = window.AppConfig?.apiUrl || document.getElementById('embedding-config')?.dataset?.apiUrl;
        const response = await fetch(`${baseUrl}/v1/embeddings`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ model: model, input: textsArray })
        });
        if (!response.ok) throw new Error("API returned error status");
        return await response.json();
    }

    function updateChart(data1, data2) {
        const ctx = document.getElementById('vectorChart').getContext('2d');
        if (window.myChart) {
            window.myChart.destroy();
        }

        // Subsampling logic: prevents browser lag with huge vectors
        const step = Math.ceil(data1.length / 250);
        const labels = [];
        const reducedData1 = [];
        const reducedData2 = [];

        for (let i = 0; i < data1.length; i += step) {
            labels.push(`Dim ${i}`);
            reducedData1.push(data1[i]);
            reducedData2.push(data2[i]);
        }

        window.myChart = new Chart(ctx, {
            type: 'line',
            data: {
                labels: labels,
                datasets: [
                    {
                        label: 'Text 1',
                        data: reducedData1,
                        borderColor: '#6366f1',
                        backgroundColor: 'rgba(99, 102, 241, 0.1)',
                        fill: true,
                        pointRadius: 0, // Hides dots for a cleaner look
                        borderWidth: 1.5
                    },
                    {
                        label: 'Text 2',
                        data: reducedData2,
                        borderColor: '#10b981',
                        backgroundColor: 'rgba(16, 185, 129, 0.1)',
                        fill: true,
                        pointRadius: 0,
                        borderWidth: 1.5
                    }
                ]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                animation: { duration: 300 }, // Snappier animation
                interaction: {
                    mode: 'index',
                    intersect: false,
                },
                scales: {
                    x: { display: false }, // Hide X axis clutter
                    y: { beginAtZero: false, grid: { color: 'rgba(0,0,0,0.05)' } }
                },
                plugins: {
                    legend: { labels: { color: '#94a3b8' } },
                    tooltip: { enabled: false } // Too dense to need tooltips
                }
            }
        });
    }
});