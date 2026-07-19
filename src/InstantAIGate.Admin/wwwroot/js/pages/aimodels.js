// wwwroot/js/pages/aimodels.js
document.addEventListener("DOMContentLoaded", () => {
    initializeLiveDownloadTelemetry();
});

async function initializeLiveDownloadTelemetry() {
    if (!window.PageConfig || !window.PageConfig.apiUrl) {
        console.error("Page configuration context missing.");
        return;
    }

    const tokenElement = document.querySelector('input[name="__RequestVerificationToken"]');
    const rvt = tokenElement ? tokenElement.value : '';

    let ticket;
    try {
        const response = await fetch('?handler=GetStreamTicket', {
            method: 'POST',
            headers: {
                'RequestVerificationToken': rvt
            }
        });

        if (!response.ok) {
            throw new Error(`Failed to obtain stream ticket. Status: ${response.status}`);
        }

        const data = await response.json();
        ticket = data.ticket;
    } catch (error) {
        console.error("Stream connection failed during ticket phase:", error);
        return;
    }

    const sseUrl = `${window.PageConfig.apiUrl}/api/admin/fetch/progress-stream?ticket=${ticket}`;
    const eventSource = new EventSource(sseUrl);

    const trackedDownloads = new Map();
    const displayNames = new Map();

    document.querySelectorAll('.data-table tbody tr').forEach(tr => {
        const titleEl = tr.querySelector('.model-title');
        const repoEl = tr.querySelector('.model-repo');
        if (titleEl && repoEl) {
            displayNames.set(repoEl.textContent.trim(), titleEl.textContent.trim());
        }
    });

    document.querySelectorAll('form[action*="handler=CancelDownload"]').forEach(form => {
        form.classList.add('cancel-download-form');
    });

    eventSource.onmessage = (event) => {
        const activeDownloads = JSON.parse(event.data);
        const currentSafeIds = new Set();

        activeDownloads.forEach(item => {
            const safeId = item.repoId.replace(/\//g, "-").replace(/\./g, "-");
            currentSafeIds.add(safeId);

            const displayName = item.displayName || displayNames.get(item.repoId) || item.repoId;

            trackedDownloads.set(safeId, {
                repoId: item.repoId,
                displayName: displayName,
                lastProgress: item.progress,
                lastStatus: item.status
            });

            const statusCell = document.getElementById(`status-cell-${safeId}`);
            const actionsCell = document.getElementById(`actions-cell-${safeId}`);

            const isCompleted = item.progress >= 100 || item.status === 'completed' || item.status === 'success';
            const isFailed = item.progress < 0 || item.status === 'failed' || item.status === 'error' || item.status === 'canceled';
            const isDownloading = !isCompleted && !isFailed;

            if (statusCell) {
                if (isCompleted) {
                    statusCell.innerHTML = `<span class="badge-status info">Ready for Allocation</span>`;
                } else if (isFailed) {
                    statusCell.innerHTML = `
                        <span class="badge-status">
                            <i class="bi bi-exclamation-circle text-danger"></i> Fetch Failed
                        </span>
                    `;
                } else {
                    statusCell.innerHTML = `
                        <div class="downloading-status-container">
                            <span class="badge-status warning">
                                <i class="bi bi-arrow-repeat animate-spin"></i> Downloading ${item.progress.toFixed(1)}%
                            </span>
                            <span class="download-file-subtext" title="${item.currentFile}">${item.currentFile}</span>
                        </div>
                    `;
                }
            }

            if (actionsCell) {
                if (actionsCell.querySelector('.unload-btn')) {
                    return;
                }

                if (isCompleted) {
                    actionsCell.innerHTML = `
                        <button onclick="openConfigDrawer('${item.repoId}', '${displayName.replace(/'/g, "\\'")}')" class="btn-secondary">
                            <i class="bi bi-power"></i> Configure & Load
                        </button>
                    `;
                } else if (isFailed) {
                    actionsCell.innerHTML = `
                        <form action="?handler=StartDownload&repoId=${encodeURIComponent(item.repoId)}" method="post" class="inline-form">
                            <input type="hidden" name="__RequestVerificationToken" value="${rvt}" />
                            <button type="submit" class="btn-outline-primary">
                                <i class="bi bi-cloud-download"></i> Retry Download
                            </button>
                        </form>
                    `;
                } else if (isDownloading) {
                    if (!actionsCell.querySelector('.cancel-download-form')) {
                        actionsCell.innerHTML = `
                            <form action="?handler=CancelDownload&repoId=${encodeURIComponent(item.repoId)}" method="post" class="inline-form cancel-download-form">
                                <input type="hidden" name="__RequestVerificationToken" value="${rvt}" />
                                <button type="submit" class="btn-danger-secondary">
                                    <i class="bi bi-slash-circle"></i> Cancel
                                </button>
                            </form>
                        `;
                    }
                }
            }
        });

        for (const [safeId, data] of trackedDownloads.entries()) {
            if (!currentSafeIds.has(safeId)) {
                const statusCell = document.getElementById(`status-cell-${safeId}`);
                const actionsCell = document.getElementById(`actions-cell-${safeId}`);

                // Relaxed threshold: if it drops from stream but was >= 95%, assume success.
                // This mitigates the backend race condition where tasks are removed before sending 100%.
                const actuallyCompleted = data.lastProgress >= 95.0 || data.lastStatus === 'completed';

                if (statusCell) {
                    if (actuallyCompleted) {
                        statusCell.innerHTML = `<span class="badge-status info">Ready for Allocation</span>`;
                    } else {
                        statusCell.innerHTML = `<span class="badge-status default">Remote Instance</span>`;
                    }
                }

                if (actionsCell && !actionsCell.querySelector('.unload-btn')) {
                    if (actuallyCompleted) {
                        actionsCell.innerHTML = `
                            <button onclick="openConfigDrawer('${data.repoId}', '${data.displayName.replace(/'/g, "\\'")}')" class="btn-secondary">
                                <i class="bi bi-power"></i> Configure & Load
                            </button>
                        `;
                    } else {
                        actionsCell.innerHTML = `
                            <form action="?handler=StartDownload&repoId=${encodeURIComponent(data.repoId)}" method="post" class="inline-form">
                                <input type="hidden" name="__RequestVerificationToken" value="${rvt}" />
                                <button type="submit" class="btn-outline-primary">
                                    <i class="bi bi-cloud-download"></i> Download
                                </button>
                            </form>
                        `;
                    }
                }

                trackedDownloads.delete(safeId);
            }
        }
    };

    eventSource.onerror = (err) => {
        console.warn("SSE stream channel dropped. Reconnecting automatically...");
        eventSource.close();

        setTimeout(() => {
            initializeLiveDownloadTelemetry();
        }, 3000);
    };
}

function showUnloadLoading(formElement) {
    const button = formElement.querySelector('.unload-btn');
    const icon = formElement.querySelector('.animate-icon');
    const text = formElement.querySelector('.btn-text');
    const actionLink = formElement.closest('td').querySelector('.console-link, .embedding-lab-link');

    if (button) {
        button.disabled = true;
        button.classList.add('opacity-75', 'cursor-not-allowed');
        if (text) text.innerText = 'Clearing VRAM...';
        if (icon) {
            icon.className = 'bi bi-arrow-repeat animate-spin';
        }
    }

    if (actionLink) {
        actionLink.classList.add('pointer-events-none', 'opacity-40');
    }

    if (typeof isDrawerLoading !== 'undefined') {
        isDrawerLoading = true;
    }
}