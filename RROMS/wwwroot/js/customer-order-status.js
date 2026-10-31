/* Owner: Member 3. Read-only checks of the signed-in customer's saved order. */
(() => {
    'use strict';
    const root = document.querySelector('[data-customer-order-status]');
    if (!root) return;
    const badge = root.querySelector('[data-order-status-badge]');
    const description = root.querySelector('[data-order-status-message]');
    const updateButton = root.querySelector('[data-order-status-update]');
    const feedback = root.querySelector('[data-order-status-feedback]');
    const live = root.querySelector('[data-order-status-live]');
    const orderId = Number(root.dataset.orderId);
    const endpoint = new URL(root.dataset.statusUrl, location.href);
    if (endpoint.origin !== location.origin || !Number.isSafeInteger(orderId) || orderId <= 0) return;
    const statuses = new Set(['Pending', 'Preparing', 'ReadyToServe', 'Served', 'Completed', 'Cancelled']);
    const terminal = status => status === 'Completed' || status === 'Cancelled';
    let displayed = root.dataset.currentStatus, dismissed = '', latest = null;
    let timer, controller, toast, stopped = false, suspended = false;

    function removeToast() { toast?.remove(); toast = null; }
    function closeToast() {
        if (latest) dismissed = latest.status;
        const focused = toast?.contains(document.activeElement);
        removeToast();
        if (focused && !updateButton.hidden) updateButton.focus({ preventScroll: true });
    }
    function showToast() {
        if (!latest || latest.status === dismissed) return;
        if (toast) {
            toast.querySelector('.yoru-toast-msg').textContent = latest.message + ' Update the status shown below.';
            return;
        }
        // Let other shared notifications finish; the inline update action stays available.
        if (document.querySelector('.yoru-toast-wrap')) return;
        toast = document.createElement('div');
        toast.className = 'yoru-toast-wrap'; toast.dataset.orderStatusToast = '';
        toast.innerHTML = '<div class="yoru-toast yoru-toast--info" role="status" aria-live="polite" aria-atomic="true">'
            + '<span class="yoru-toast-seal" aria-hidden="true"><i class="bi bi-info-lg"></i></span>'
            + '<div class="yoru-toast-body"><p class="yoru-toast-title"><span class="yoru-toast-jp">お知らせ</span> Order status updated</p><p class="yoru-toast-msg"></p></div>'
            + '<button type="button" class="order-status-toast-action">Update status</button>'
            + '<button type="button" class="yoru-toast-close" aria-label="Close notification"><i class="bi bi-x-lg" aria-hidden="true"></i></button></div>';
        toast.querySelector('.yoru-toast-msg').textContent = latest.message + ' Update the status shown below.';
        toast.querySelector('.order-status-toast-action').addEventListener('click', () => checkStatus(true));
        toast.querySelector('.yoru-toast-close').addEventListener('click', closeToast);
        toast.addEventListener('keydown', event => {
            if (event.key === 'Escape') { event.preventDefault(); closeToast(); }
        });
        document.body.append(toast);
    }
    function setBusy(busy) {
        updateButton.disabled = busy;
        updateButton.querySelector('span').textContent = busy ? 'Updating…' : 'Update status';
        if (busy) root.setAttribute('aria-busy', 'true'); else root.removeAttribute('aria-busy');
        const action = toast?.querySelector('.order-status-toast-action');
        if (action) { action.disabled = busy; action.textContent = busy ? 'Updating…' : 'Update status'; }
    }
    function pause(message) {
        stopped = true; clearTimeout(timer); removeToast();
        updateButton.hidden = true; feedback.textContent = message; feedback.hidden = false;
    }
    function applyStatus(data) {
        const hadFocus = toast?.contains(document.activeElement) || document.activeElement === updateButton;
        // Only replace the badge and explanatory text; photos, prices and page position stay intact.
        for (const status of statuses) badge.classList.remove('status-' + status);
        badge.classList.add('status-' + data.status);
        // Keep the shared status icon: swap it from the page's templates, then update only the label.
        const icon = badge.querySelector('[data-order-status-icon]'), template = root.querySelector(`template[data-order-status-icon-template="${data.status}"]`);
        if (icon && template) icon.replaceChildren(template.content.cloneNode(true));
        const text = badge.querySelector('[data-order-status-label]'); if (text) text.textContent = data.label; else badge.textContent = data.label;
        displayed = data.status; root.dataset.currentStatus = displayed;
        description.textContent = data.message; latest = null; dismissed = '';
        updateButton.hidden = true; removeToast();
        live.textContent = 'Order status: ' + data.label + '. ' + data.message;
        if (hadFocus) { badge.tabIndex = -1; badge.focus({ preventScroll: true }); }
    }
    function schedule() {
        clearTimeout(timer);
        if (!stopped && !suspended && !document.hidden && !terminal(displayed))
            timer = setTimeout(() => checkStatus(), 15000);
    }
    async function checkStatus(apply = false) {
        if (stopped || suspended || (!apply && document.hidden)) return;
        if (controller) { if (!apply) return; controller.abort(); }
        clearTimeout(timer);
        const current = new AbortController(); controller = current;
        const timeout = setTimeout(() => current.abort(), 10000);
        if (apply) setBusy(true);
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
            if (apply) applyStatus(data);
            else if (data.status !== displayed) {
                latest = data; updateButton.hidden = false; showToast();
            } else {
                latest = null; dismissed = ''; updateButton.hidden = true; removeToast();
            }
        } catch {
            if (controller !== current || suspended) return;
            feedback.textContent = 'Could not check the latest status. Select Update status to try again.';
            feedback.hidden = false; updateButton.hidden = false;
        } finally {
            clearTimeout(timeout);
            if (controller === current) { controller = null; setBusy(false); schedule(); }
        }
    }
    updateButton.addEventListener('click', () => checkStatus(true));
    document.addEventListener('visibilitychange', () => {
        if (document.hidden) clearTimeout(timer); else { checkStatus(); schedule(); }
    });
    addEventListener('pagehide', () => { suspended = true; clearTimeout(timer); controller?.abort(); removeToast(); });
    addEventListener('pageshow', event => { if (event.persisted) { suspended = false; checkStatus(); } });
    schedule();
})();
