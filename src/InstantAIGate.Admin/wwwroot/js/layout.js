// wwwroot/js/layout.js
(function () {
    'use strict';

    function initThemeToggle() {
        var themeToggleBtn = document.getElementById('theme-toggle');
        var icon = document.getElementById('theme-toggle-icon');
        if (!themeToggleBtn) return;

        function updateIcon() {
            if (!icon) return;
            if (document.documentElement.classList.contains('dark')) {
                icon.className = 'bi bi-sun';
            } else {
                icon.className = 'bi bi-moon';
            }
        }
        updateIcon();

        themeToggleBtn.addEventListener('click', function () {
            if (document.documentElement.classList.contains('dark')) {
                document.documentElement.classList.remove('dark');
                localStorage.setItem('theme', 'light');
            } else {
                document.documentElement.classList.add('dark');
                localStorage.setItem('theme', 'dark');
            }
            updateIcon();
        });
    }

    function initNavHighlight() {
        var links = document.querySelectorAll('.nav-link');
        if (!links || links.length === 0) return;
        var path = window.location.pathname || '/';
        if (path.length > 1 && path.endsWith('/')) path = path.slice(0, -1);

        links.forEach(function (el) {
            var target = el.getAttribute('data-path') || el.getAttribute('href') || '';
            if (target.length > 1 && target.endsWith('/')) target = target.slice(0, -1);

            if ((target === '/' && (path === '/' || path === '')) || (target && target !== '/' && path.indexOf(target) === 0)) {
                el.classList.add('active');
            } else {
                el.classList.remove('active');
            }
        });
    }

    function initMobileMenu() {
        var toggleBtn = document.getElementById('mobile-menu-toggle');
        var sidebar = document.getElementById('sidebar-menu');
        var backdrop = document.getElementById('sidebar-backdrop');
        var icon = document.getElementById('mobile-menu-icon');

        if (!toggleBtn || !sidebar || !backdrop) return;

        function toggleMenu() {
            var isOpen = sidebar.classList.contains('open');
            if (isOpen) {
                sidebar.classList.remove('open');
                backdrop.classList.add('hidden');
                if (icon) { icon.className = 'bi bi-list'; }
            } else {
                sidebar.classList.add('open');
                backdrop.classList.remove('hidden');
                if (icon) { icon.className = 'bi bi-x-lg'; }
            }
        }

        toggleBtn.addEventListener('click', toggleMenu);
        backdrop.addEventListener('click', toggleMenu);

        var navLinks = sidebar.querySelectorAll('.nav-link');
        navLinks.forEach(function (link) {
            link.addEventListener('click', function () {
                if (window.innerWidth < 768 && sidebar.classList.contains('open')) {
                    toggleMenu();
                }
            });
        });
    }

    function getApiUrlFromMeta() {
        var meta = document.querySelector('meta[name="app-api-url"]');
        return meta ? meta.getAttribute('content') || '' : '';
    }

    function initSignalR() {
        var badge = document.getElementById('global-signalr-badge');
        var apiUrl = getApiUrlFromMeta() || 'http://127.0.0.1:7050';
        if (!badge) return;

        var BADGE_STATE_KEY = 'signalr-badge-state-raw';
        var currentStatusState = 'offline';

        try {
            var cachedState = localStorage.getItem(BADGE_STATE_KEY);
            if (cachedState) {
                currentStatusState = cachedState;
                applyBadgeDOM(cachedState);
            }
        } catch (e) { }

        var reconnectTimer = null;

        function applyBadgeDOM(state) {
            badge.style.backgroundColor = "";
            badge.style.color = "";

            if (state === 'connected') {
                badge.className = "signalr-badge connected";
                badge.innerHTML = '<span class="badge-dot animate-pulse"></span><span class="hidden-sm">Ready</span><span class="sm-hidden">✓</span>';
            } else if (state === 'reconnecting') {
                badge.className = "signalr-badge reconnecting";
                badge.innerHTML = '<span class="badge-dot animate-pulse"></span><span class="hidden-sm">Reconnecting...</span><span class="sm-hidden">...</span>';
            } else if (state === 'updating') {
                badge.className = "signalr-badge updating-drivers";
                badge.style.backgroundColor = "#f39c12";
                badge.style.color = "#fff";
                badge.innerHTML = '<span class="badge-dot animate-pulse" style="background-color: #fff;"></span><span class="hidden-sm">Updating drivers...</span><span class="sm-hidden">⏳</span>';
            } else {
                badge.className = "signalr-badge offline";
                badge.innerHTML = '<span class="badge-dot"></span><span class="hidden-sm">Offline</span><span class="sm-hidden">✗</span>';
            }
        }

        function setStatus(state) {
            currentStatusState = state;
            applyBadgeDOM(state);
            try {
                localStorage.setItem(BADGE_STATE_KEY, state);
            } catch (e) { }

            if (state === 'connected') {
                window.dispatchEvent(new Event('signalr-connected'));
            }
        }

        if (!window.HubConnection && window.signalR) {
            window.HubConnection = new signalR.HubConnectionBuilder()
                .withUrl(apiUrl + '/hubs/telemetry')
                .withAutomaticReconnect([0, 2000, 5000, 10000])
                .build();

            window.HubConnection.onreconnecting(function (error) { setStatus('reconnecting'); });

            window.HubConnection.onreconnected(function (id) {
                if (currentStatusState !== 'updating') {
                    setStatus('connected');
                }
                // КРИТИЧНО: При переподключении снова подписываемся на метрики!
                window.HubConnection.invoke('SubscribeToMetrics').catch(function (e) { console.error(e); });
            });

            window.HubConnection.onclose(function (error) { setStatus('offline'); reconnectTimer = setTimeout(startSignalRConnection, 5000); });

            window.HubConnection.on("ReceiveTelemetry", function (snapshot) {
                if (!snapshot) return;

                if (snapshot.isExtractingDrivers) {
                    if (currentStatusState !== 'updating') {
                        setStatus('updating');
                    }
                } else {
                    if (currentStatusState !== 'connected') {
                        setStatus('connected');
                    }
                }
            });
        }

        function startSignalRConnection() {
            if (reconnectTimer) clearTimeout(reconnectTimer);
            if (!window.HubConnection) return;

            var state = window.HubConnection.state;
            if (state === 'Connected' || state === 2) {
                if (currentStatusState !== 'updating') setStatus('connected');
                return;
            }
            if (state === 'Connecting' || state === 'Reconnecting' || state === 1 || state === 3) {
                reconnectTimer = setTimeout(startSignalRConnection, 5000);
                return;
            }

            window.HubConnection.start()
                .then(function () {
                    if (currentStatusState !== 'updating') {
                        setStatus('connected');
                    }

                    // КРИТИЧНО: Глобально подписываем КАЖДУЮ страницу на получение событий!
                    return window.HubConnection.invoke('SubscribeToMetrics');
                })
                .then(function () {
                    return window.HubConnection.invoke('Ping').catch(function () { return 'No Ping'; });
                })
                .then(function (reply) {
                    if (reply && reply !== 'No Ping') console.log('SignalR Health Check:', reply);
                })
                .catch(function (err) {
                    setStatus('offline');
                    reconnectTimer = setTimeout(startSignalRConnection, 5000);
                });
        }

        startSignalRConnection();
    }

    document.addEventListener('DOMContentLoaded', function () {
        try { initThemeToggle(); } catch (e) { console.error(e); }
        try { initNavHighlight(); } catch (e) { console.error(e); }
        try { initMobileMenu(); } catch (e) { console.error(e); }
        try { initSignalR(); } catch (e) { console.error(e); }
    });
})();