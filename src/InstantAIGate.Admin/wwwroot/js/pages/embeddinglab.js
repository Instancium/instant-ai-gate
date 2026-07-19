document.addEventListener('DOMContentLoaded', async () => {
    // --- DOM Elements ---
    const analyzeBtn = document.getElementById('analyze-btn');
    const embeddingConfigEl = document.getElementById('embedding-config');
    const apiUrl = window.AppConfig?.apiUrl || embeddingConfigEl?.dataset?.apiUrl;
    const activeModelDisplay = document.getElementById('active-model-display');

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

    // --- SignalR Global Hook for UI Updates ---
    function handleTelemetryUpdate(telemetryData) {
        if (!telemetryData) return;

        const modelsArray = telemetryData?.models || telemetryData?.Models || [];
        // True active models have maxParallelUsers > 0
        const activeModel = modelsArray.find(m => (m.maxParallelUsers > 0) || (m.MaxParallelUsers > 0));
        const activeRepoId = activeModel ? (activeModel.repoId || activeModel.RepoId) : null;

        const displayText = activeRepoId || "No active pipeline target";

        if (activeModelDisplay) {
            if (activeModelDisplay.tagName === 'INPUT' || activeModelDisplay.tagName === 'TEXTAREA' || activeModelDisplay.tagName === 'SELECT') {
                activeModelDisplay.value = displayText;
            } else {
                activeModelDisplay.innerText = displayText;
            }
        }

        if (analyzeBtn) {
            if (activeRepoId) {
                analyzeBtn.disabled = false;
                analyzeBtn.innerText = "Analyze & Compare";
            } else {
                analyzeBtn.disabled = true;
                analyzeBtn.innerText = "No models available";
            }
        }
    }

    function attachToGlobalSignalR() {
        if (window.HubConnection) {
            window.HubConnection.on("ReceiveTelemetry", handleTelemetryUpdate);
        } else {
            setTimeout(attachToGlobalSignalR, 100);
        }
    }

    attachToGlobalSignalR();

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

    // --- Analysis Action ---
    if (analyzeBtn) {
        analyzeBtn.addEventListener('click', async () => {
            const text1 = document.getElementById('text1').value.trim();
            const text2 = document.getElementById('text2').value.trim();

            const currentVal = (activeModelDisplay?.value || activeModelDisplay?.innerText || "").trim();

            const gaugeFill = document.getElementById('gauge-fill');
            const similarityText = document.getElementById('similarity-score');

            if (currentVal === "No active pipeline target" || currentVal === "") {
                showAlert("Please wait for an active pipeline model to be initialized.", "warning");
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

                const startTime = performance.now();

                // Send auto-routed as the model ID to let the adapter handle it
                const responseData = await getEmbeddingsBatch([text1, text2], "auto-routed");

                const latencyMs = performance.now() - startTime;

                if (!responseData.data || responseData.data.length < 2) {
                    throw new Error("Invalid response structure from server.");
                }

                const vec1 = responseData.data[0].embedding;
                const vec2 = responseData.data[1].embedding;

                if (metaDimensions) metaDimensions.innerText = vec1.length;
                if (metaTokens) {
                    metaTokens.innerText = responseData.usage?.total_tokens || responseData.usage?.totalTokens || "N/A";
                }
                if (metaLatency) metaLatency.innerText = `${Math.round(latencyMs)} ms`;

                const similarity = calculateCosineSimilarity(vec1, vec2);

                const baseline = 0.5;
                let percentage = Math.round(((similarity - baseline) / (1.0 - baseline)) * 100);
                percentage = Math.max(0, Math.min(100, percentage));

                if (similarityText) similarityText.innerText = `${percentage}%`;
                if (gaugeFill) {
                    gaugeFill.style.width = `${percentage}%`;
                    let color = percentage < 30 ? "#ef4444" : (percentage < 60 ? "#f59e0b" : "#10b981");
                    gaugeFill.style.background = `linear-gradient(to right, ${color}, #a7f3d0)`;
                }

                updateCharts(vec1, vec2, similarity);

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

    // --- Update Both Charts ---
    function updateCharts(vec1, vec2, similarity) {
        updateLinearChart(vec1, vec2);
        updateRadar(vec1, vec2, similarity);
    }

    // --- Linear Chart (Existing) ---
    function updateLinearChart(data1, data2) {
        const ctx = document.getElementById('vectorChart').getContext('2d');
        if (window.myChart) {
            window.myChart.destroy();
        }

        const numSegments = 32;
        const segmentSize = Math.ceil(data1.length / numSegments);

        const labels = [];
        const segmented1 = [];
        const segmented2 = [];
        const differences = [];

        for (let i = 0; i < numSegments; i++) {
            labels.push(`Seg ${i + 1}`);

            let sum1 = 0, sum2 = 0;
            const start = i * segmentSize;
            const end = Math.min(start + segmentSize, data1.length);

            for (let j = start; j < end; j++) {
                sum1 += Math.abs(data1[j] || 0);
                sum2 += Math.abs(data2[j] || 0);
            }

            const avg1 = sum1 / (end - start);
            const avg2 = sum2 / (end - start);

            segmented1.push(avg1);
            segmented2.push(avg2);
            differences.push(Math.abs(avg1 - avg2));
        }

        const topDiffs = differences
            .map((diff, idx) => ({ diff, idx }))
            .sort((a, b) => b.diff - a.diff)
            .slice(0, 5)
            .map(d => d.idx);

        const maxDiff = Math.max(...differences);
        const diffColors = differences.map(d => {
            const intensity = d / maxDiff;
            return `rgba(239, 68, 68, ${intensity * 0.6})`;
        });

        window.myChart = new Chart(ctx, {
            type: 'bar',
            data: {
                labels: labels,
                datasets: [
                    {
                        label: 'Text 1 (Avg Magnitude)',
                        data: segmented1,
                        backgroundColor: 'rgba(99, 102, 241, 0.7)',
                        borderColor: '#6366f1',
                        borderWidth: 1,
                        order: 2
                    },
                    {
                        label: 'Text 2 (Avg Magnitude)',
                        data: segmented2,
                        backgroundColor: 'rgba(16, 185, 129, 0.7)',
                        borderColor: '#10b981',
                        borderWidth: 1,
                        order: 2
                    },
                    {
                        label: 'Difference (|T1 - T2|)',
                        data: differences,
                        type: 'line',
                        borderColor: 'rgba(239, 68, 68, 0.8)',
                        backgroundColor: 'rgba(239, 68, 68, 0.1)',
                        borderWidth: 2,
                        pointRadius: 3,
                        pointBackgroundColor: differences.map((d, i) =>
                            topDiffs.includes(i) ? '#ef4444' : 'rgba(239, 68, 68, 0.5)'
                        ),
                        pointBorderColor: '#ef4444',
                        fill: true,
                        tension: 0.3,
                        order: 1
                    }
                ]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                animation: { duration: 500 },
                interaction: {
                    mode: 'index',
                    intersect: false,
                },
                scales: {
                    x: {
                        grid: { color: 'rgba(0,0,0,0.05)' },
                        ticks: {
                            color: '#94a3b8',
                            maxRotation: 45,
                            callback: function (value, index) {
                                return index % 4 === 0 ? this.getLabelForValue(value) : '';
                            }
                        }
                    },
                    y: {
                        beginAtZero: true,
                        grid: { color: 'rgba(0,0,0,0.05)' },
                        ticks: { color: '#94a3b8' }
                    }
                },
                plugins: {
                    legend: {
                        labels: { color: '#94a3b8' },
                        position: 'top'
                    },
                    tooltip: {
                        callbacks: {
                            title: function (context) {
                                const idx = context[0].dataIndex;
                                const start = idx * segmentSize;
                                const end = Math.min(start + segmentSize, data1.length);
                                return `Dimensions ${start}-${end}`;
                            },
                            label: function (context) {
                                const label = context.dataset.label || '';
                                const value = context.parsed.y.toFixed(4);
                                return `${label}: ${value}`;
                            }
                        }
                    }
                }
            }
        });
    }

    // --- Radar Chart (With Baseline Normalization) ---
    async function updateRadar(vec1, vec2, similarity) {
        const scannerSweep = document.getElementById('radar-scanner-sweep');
        const groupA = document.getElementById('radar-group-a');
        const groupB = document.getElementById('radar-group-b');
        const dotA = document.getElementById('radar-dot-a');
        const dotB = document.getElementById('radar-dot-b');
        const laser = document.getElementById('radar-laser');
        const scoreDisplay = document.getElementById('radar-score-display');
        const distanceDisplay = document.getElementById('radar-distance-display');

        const CENTER = 100;
        const RADIUS = 85;
        const ORBIT_RADIUS = RADIUS * 0.7;

        scannerSweep.style.display = 'block';
        groupA.classList.add('radar-hidden');
        groupB.classList.add('radar-hidden');
        laser.style.opacity = '0';
        scoreDisplay.innerText = '---';
        distanceDisplay.innerText = '---';

        const baseline = 0.5;
        let normalizedSimilarity = (similarity - baseline) / (1.0 - baseline);
        normalizedSimilarity = Math.max(0, Math.min(1, normalizedSimilarity));

        const angleA = 0;
        const svgAx = CENTER + ORBIT_RADIUS * Math.cos(angleA);
        const svgAy = CENTER + ORBIT_RADIUS * Math.sin(angleA);

        const angleB = Math.PI * (1 - normalizedSimilarity);
        const svgBx = CENTER + ORBIT_RADIUS * Math.cos(angleB);
        const svgBy = CENTER + ORBIT_RADIUS * Math.sin(angleB);

        await delay(800);

        dotA.setAttribute('cx', svgAx);
        dotA.setAttribute('cy', svgAy);
        document.getElementById('radar-label-a').setAttribute('x', svgAx);
        document.getElementById('radar-label-a').setAttribute('y', svgAy - 10);
        groupA.classList.remove('radar-hidden');

        await delay(300);

        dotB.setAttribute('cx', svgBx);
        dotB.setAttribute('cy', svgBy);
        document.getElementById('radar-label-b').setAttribute('x', svgBx);
        document.getElementById('radar-label-b').setAttribute('y', svgBy - 10);
        groupB.classList.remove('radar-hidden');

        await delay(400);

        laser.setAttribute('x1', svgAx);
        laser.setAttribute('y1', svgAy);
        laser.setAttribute('x2', svgBx);
        laser.setAttribute('y2', svgBy);
        laser.style.opacity = '1';
        scannerSweep.style.display = 'none';

        scoreDisplay.innerText = similarity.toFixed(3);

        const pixelDistance = Math.sqrt(
            Math.pow(svgBx - svgAx, 2) + Math.pow(svgBy - svgAy, 2)
        ).toFixed(1);
        distanceDisplay.innerText = pixelDistance;
    }

    function delay(ms) {
        return new Promise(resolve => setTimeout(resolve, ms));
    }
});