/* Owner: Member 3. US24 / US27: live status of the signed-in customer's own order.
   SignalR sends a short "status changed" signal; the page then reads the status from the
   existing Status endpoint (which re-checks the owner). Checking every 15 seconds is used
   only while the live connection is unavailable. */
(() => {
    'use strict';
    const root = document.querySelector('[data-customer-order-status]');
    if (!root) return;
    const badge = root.querySelector('[data-order-status-badge]');
    const description = root.querySelector('[data-order-status-message]');
    const updateButton = root.querySelector('[data-order-status-update]');
    const feedback = root.querySelector('[data-order-status-feedback]');
    const live = root.querySelector('[data-order-status-live]');
    const progress = root.querySelector('[data-order-progress]');
    const orderId = Number(root.dataset.orderId);
    const endpoint = new URL(root.dataset.statusUrl, location.href);
    if (endpoint.origin !== location.origin || !Number.isSafeInteger(orderId) || orderId <= 0) return;
    const flow = ['Pending', 'Preparing', 'ReadyToServe', 'Served', 'Completed'];
    const statuses = new Set([...flow, 'Cancelled']);
    const terminal = status => status === 'Completed' || status === 'Cancelled';
    let displayed = root.dataset.currentStatus;
    let timer, controller, toast, toastTimer, connection = null;
    let connected = false, again = false, stopped = false, suspended = false;

    function removeToast() { clearTimeout(toastTimer); toast?.remove(); toast = null; }
    function showToast(data) {
        removeToast();
        // Let other shared notifications finish; the badge and progress bar are already updated.
        if (document.querySelector('.yoru-toast-wrap')) return;
        toast = document.createElement('div');
        toast.className = 'yoru-toast-wrap'; toast.dataset.orderStatusToast = '';
        // The hidden live region announces the change, so the toast itself is not a live region.
        toast.innerHTML = '<div class="yoru-toast yoru-toast--info">'
            + '<span class="yoru-toast-seal" aria-hidden="true"><i class="bi bi-info-lg"></i></span>'
            + '<div class="yoru-toast-body"><p class="yoru-toast-title"><span class="yoru-toast-jp">お知らせ</span> Order status updated</p><p class="yoru-toast-msg"></p></div>'
            + '<button type="button" class="yoru-toast-close" aria-label="Close notification"><i class="bi bi-x-lg" aria-hidden="true"></i></button></div>';
        toast.querySelector('.yoru-toast-msg').textContent = data.label + ' · ' + data.message;
        toast.querySelector('.yoru-toast-close').addEventListener('click', removeToast);
        toast.addEventListener('keydown', event => {
            if (event.key === 'Escape') { event.preventDefault(); removeToast(); }
        });
        document.body.append(toast);
        toastTimer = setTimeout(removeToast, 6000);
    }
    function setBusy(busy) {
        updateButton.disabled = busy;
        updateButton.querySelector('span').textContent = busy ? 'Updating…' : 'Update status';
        if (busy) root.setAttribute('aria-busy', 'true'); else root.removeAttribute('aria-busy');
    }
    function stopLive() {
        connected = false;
        const current = connection; connection = null;
        current?.stop().catch(() => { });
    }
    function pause(message) {
        stopped = true; clearTimeout(timer); removeToast(); stopLive();
        updateButton.hidden = true; feedback.textContent = message; feedback.hidden = false;
    }
    function renderProgress(status) {
        if (!progress) return;
        const index = flow.indexOf(status);
        progress.dataset.state = status;
        progress.querySelectorAll('[data-step]').forEach((step, i) => {
            const done = index >= 0 && i < index, current = i === index;
            step.classList.toggle('is-done', done);
            step.classList.toggle('is-current', current);
            if (current) step.setAttribute('aria-current', 'step'); else step.removeAttribute('aria-current');
            const state = step.querySelector('[data-step-state]');
            if (state) state.textContent = done ? ' (done)' : current ? ' (current)' : '';
        });
    }
    function applyStatus(data, notify) {
        const changed = data.status !== displayed;
        // Only replace the badge, progress and explanatory text; photos, prices and page position stay intact.
        for (const status of statuses) badge.classList.remove('status-' + status);
        badge.classList.add('status-' + data.status);
        // Keep the shared status icon: swap it from the page's templates, then update only the label.
        const icon = badge.querySelector('[data-order-status-icon]');
        const template = root.querySelector(`template[data-order-status-icon-template="${data.status}"]`);
        if (icon && template) icon.replaceChildren(template.content.cloneNode(true));
        const text = badge.querySelector('[data-order-status-label]');
        if (text) text.textContent = data.label; else badge.textContent = data.label;
        displayed = data.status; root.dataset.currentStatus = displayed;
        description.textContent = data.message;
        updateButton.hidden = true; feedback.hidden = true;
        renderProgress(displayed);
        if (changed) {
            live.textContent = 'Order status: ' + data.label + '. ' + data.message;
            if (notify) showToast(data);
        }
        // Completed and Cancelled never change again, so live updates can stop.
        if (terminal(displayed)) { clearTimeout(timer); stopLive(); }
    }
    function schedule() {
        clearTimeout(timer);
        if (!stopped && !suspended && !connected && !document.hidden && !terminal(displayed))
            timer = setTimeout(() => checkStatus(), 15000);
    }
    async function checkStatus(manual = false) {
        if (stopped || suspended || (!manual && document.hidden)) return;
        if (controller) {
            // A live signal during a check: check once more afterwards so the newest status is never missed.
            if (!manual) { again = true; return; }
            controller.abort();
        }
        clearTimeout(timer);
        const current = new AbortController(); controller = current;
        const timeout = setTimeout(() => current.abort(), 10000);
        if (manual) setBusy(true);
        try {
            const response = await fetch(endpoint, {
                credentials: 'same-origin', cache: 'no-store', signal: current.signal,
                headers: { 'X-Requested-With': 'XMLHttpRequest', Accept: 'application/json' }
            });
            if (controller !== current || suspended) return;
            if (response.redirected || response.status === 401 || response.status === 403) {
                pause('Your session has expired or changed. Refresh this page and sign in again.'); return;
            }
            if (response.status === 404) { pause('This order is no longer available. Refresh this page to continue.'); return; }
            if (!response.ok || !response.headers.get('content-type')?.includes('json')) throw new Error('Status unavailable');
            const data = await response.json();
            if (controller !== current || suspended) return;
            if (data.orderId !== orderId || !statuses.has(data.status) || typeof data.label !== 'string' || typeof data.message !== 'string')
                throw new Error('Invalid status response');
            feedback.hidden = true;
            if (manual || data.status !== displayed) applyStatus(data, !manual);
        } catch {
            if (controller !== current || suspended) return;
            feedback.textContent = 'Could not check the latest status. Select Update status to try again.';
            feedback.hidden = false; updateButton.hidden = false;
        } finally {
            clearTimeout(timeout);
            if (controller === current) {
                controller = null; setBusy(false);
                if (again && !stopped && !suspended) { again = false; checkStatus(); }
                else { again = false; schedule(); }
            }
        }
    }
    async function startLive() {
        const hubUrl = root.dataset.hubUrl;
        if (connection || stopped || suspended || !hubUrl || !window.signalR || terminal(displayed)) return;
        const hub = new signalR.HubConnectionBuilder().withUrl(hubUrl)
            .withAutomaticReconnect().configureLogging(signalR.LogLevel.None).build();
        connection = hub;
        const watch = async () => { connected = connection === hub && await hub.invoke('WatchOrder', orderId) === true; };
        hub.on('OrderStatusChanged', data => {
            if (data && data.orderId === orderId && statuses.has(data.status) && data.status !== displayed) checkStatus();
        });
        hub.onreconnecting(() => { connected = false; schedule(); });
        hub.onreconnected(async () => {
            try { await watch(); } catch { connected = false; }
            // Updates may have been missed while offline.
            if (connected) clearTimeout(timer);
            checkStatus();
        });
        hub.onclose(() => { if (connection === hub) { connection = null; connected = false; schedule(); } });
        try {
            await hub.start();
            await watch();
        } catch {
            connected = false;
        }
        if (connected) { clearTimeout(timer); checkStatus(); } else schedule();
    }
    updateButton.addEventListener('click', () => checkStatus(true));
    document.addEventListener('visibilitychange', () => {
        if (document.hidden) clearTimeout(timer); else { checkStatus(); schedule(); }
    });
    addEventListener('pagehide', () => {
        suspended = true; clearTimeout(timer); controller?.abort(); removeToast(); stopLive();
    });
    addEventListener('pageshow', event => {
        if (event.persisted) { suspended = false; checkStatus(); startLive(); }
    });
    schedule();
    startLive();
})();
