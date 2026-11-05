// Owner: Member 2 — US13. Server/customer strings always use textContent.
(() => {
    'use strict';
    const page = document.getElementById('myReservations');
    if (!page) return;
    const byId = id => document.getElementById(id);
    const cfg = JSON.parse(byId('myrConfig').textContent);
    const items = new Map(cfg.items.map(item => [Number(item.id), item]));
    const details = byId('myrDetails');
    const confirm = byId('myrConfirm');
    const confirmSubmit = byId('myrConfirmSubmit');
    const keepButton = byId('myrKeepReservation');
    const cancelButton = byId('myrOpenCancel');
    const cancelError = byId('myrCancelError');
    const cancelNote = byId('myrCancelNote');
    const ruleNote = byId('myrRuleNote');
    const requestBox = byId('myrRequestBox');
    const readMore = byId('myrReadMore');
    const listHeading = byId('myrListHeading');
    const clockOffset = cfg.serverNowMs - Date.now();
    let current = null;
    let lastOpener = null;
    let busy = false;

    const restaurantNow = () => Date.now() + clockOffset;
    // The wax seal shrinks its text for longer table numbers (the server only sends a number once staff have confirmed).
    const sealSize = n => n.length <= 3 ? 's3' : n.length === 4 ? 's4' : n.length === 5 ? 's5' : n.length === 6 ? 's6' : 's7';
    const canCancel = item => Boolean(item && item.canCancel &&
        ['Pending', 'Confirmed'].includes(item.status) && item.reservedAtMs > restaurantNow());
    // Lock page scroll while a dialog is open, but keep the page from jumping sideways
    // when the scrollbar disappears (padding takes the scrollbar's place).
    const syncScroll = () => {
        const root = document.documentElement;
        const open = details.open || confirm.open;
        if (open && !root.classList.contains('myr-has-dialog'))
            root.style.setProperty('--myr-sbw', Math.max(0, window.innerWidth - root.clientWidth) + 'px');
        root.classList.toggle('myr-has-dialog', open);
        if (!open) root.style.removeProperty('--myr-sbw');
    };
    const showError = message => { cancelError.textContent = message; cancelError.hidden = false; };

    function syncQuickCancel() {
        items.forEach(item => {
            if (canCancel(item)) return;
            page.querySelectorAll('[data-cancel="' + Number(item.id) + '"]').forEach(b => { b.hidden = true; });
        });
    }

    function refreshCancellationState() {
        syncQuickCancel();
        if (!current) return;
        const allowed = canCancel(current);
        cancelButton.hidden = !allowed;
        ruleNote.hidden = !allowed;
        cancelNote.hidden = allowed;
        cancelNote.textContent = current.cancelMessage || 'Cancellation is unavailable after the scheduled reservation time.';
        confirmSubmit.disabled = busy || !allowed;
    }

    function openConfirm(item, opener) {
        if (!item || busy) return;
        current = item;
        if (opener) lastOpener = opener;
        refreshCancellationState();
        if (!canCancel(current)) return;
        byId('myrConfirmReference').textContent = current.reference;
        byId('myrConfirmDate').textContent = current.date + ' · ' + current.time + ' · ' + current.guests + (current.guests === 1 ? ' guest' : ' guests');
        byId('myrCancelId').value = String(current.id);
        cancelError.hidden = true;
        confirmSubmit.textContent = 'Yes, cancel reservation';
        confirm.showModal();
        keepButton.focus();
        syncScroll();
    }

    // Long special requests: show 2 lines + "Read more"; opened, the text scrolls inside its own small box.
    function setupRequestBox() {
        requestBox.classList.remove('is-open');
        requestBox.scrollTop = 0;
        readMore.textContent = 'Read more';
        readMore.setAttribute('aria-expanded', 'false');
        readMore.hidden = !(requestBox.scrollHeight > requestBox.clientHeight + 1);
    }
    readMore.addEventListener('click', () => {
        const open = requestBox.classList.toggle('is-open');
        requestBox.scrollTop = 0;
        readMore.textContent = open ? 'Show less' : 'Read more';
        readMore.setAttribute('aria-expanded', String(open));
    });

    function openDetails(item, opener) {
        if (!item || busy) return;
        current = item;
        lastOpener = opener;
        const values = {
            ...item,
            customerName: cfg.customerName,
            weekday: '(' + item.weekday + ')',
            guestText: item.guests + (item.guests === 1 ? ' guest' : ' guests')
        };
        details.querySelectorAll('[data-fill]').forEach(el => {
            el.textContent = String(values[el.dataset.fill] ?? '');
        });
        // The dining area line (icon and name) is hidden when the table type has no area.
        const areaLine = details.querySelector('[data-area-line]');
        if (areaLine) {
            areaLine.hidden = !item.area;
            const use = areaLine.querySelector('use');
            if (use) use.setAttribute('href', '#area-' + (/^[a-z-]+$/.test(item.areaIcon || '') ? item.areaIcon : 'map-pin'));
        }
        // The arrival time shows AM/PM smaller than the clock.
        const arrival = details.querySelector('[data-fill="time"]');
        const clock = /^(.+?)\s*(AM|PM)$/i.exec(item.time || '');
        if (arrival && clock) {
            const ampm = document.createElement('em');
            ampm.className = 'myr-ampm';
            ampm.textContent = clock[2].toUpperCase();
            arrival.textContent = clock[1] + ' ';
            arrival.append(ampm);
        }
        const confirmed = item.status === 'Confirmed';
        const seated = item.status === 'Seated';
        const status = byId('myrDetailsStatus');
        status.classList.toggle('is-confirmed', confirmed);
        status.classList.toggle('is-seated', seated);
        status.classList.toggle('is-pending', !confirmed && !seated);
        const icon = confirmed ? '#myr-check' : seated ? '#myr-table' : '#myr-hourglass';
        byId('myrStatusIcon').setAttribute('href', icon);
        byId('myrStatusLargeIcon').setAttribute('href', icon);
        byId('myrStatusCaption').textContent = seated
            ? 'You are being served. Enjoy your evening.'
            : confirmed ? 'We look forward to welcoming you.' : 'Awaiting restaurant confirmation.';
        const seal = byId('myrDetailSeal');
        const table = item.tableNumber ? String(item.tableNumber) : '';
        seal.hidden = !table;
        byId('myrTableNote').hidden = !table;
        if (table) {
            const no = byId('myrDetailSealNo');
            no.textContent = table;
            no.className = sealSize(table);
            seal.classList.toggle('is-long', table.length >= 6);
            seal.setAttribute('aria-label', 'Table ' + table);
        }
        byId('myrSpendAccepted').hidden = !(item.hasMinimumSpend && item.spendAccepted);
        // US36: the booking PDF. The page only accepts upcoming Pending / Confirmed bookings, so no button for the others.
        const pdf = byId('myrPdf');
        const hasPdf = ['Pending', 'Confirmed'].includes(item.status) && item.reservedAtMs > restaurantNow();
        pdf.hidden = !hasPdf;
        if (hasPdf) pdf.href = '/Customer/ReservationConfirmation?id=' + encodeURIComponent(item.id);
        refreshCancellationState();
        details.classList.remove('is-closing');
        details.showModal();
        details.scrollTop = 0;
        setupRequestBox();
        byId('myrDetailsTitle').focus({ preventScroll: true });
        syncScroll();
    }

    page.addEventListener('click', event => {
        const button = event.target.closest('[data-view]');
        if (button) { openDetails(items.get(Number(button.dataset.view)), button); return; }
        const quick = event.target.closest('[data-cancel]');
        if (quick) openConfirm(items.get(Number(quick.dataset.cancel)), quick);
    });
    // Fade/slide the ticket out, then close the native dialog.
    function closeDetails() {
        if (!details.open) return;
        if (details.classList.contains('is-closing')) return;
        if (window.matchMedia('(prefers-reduced-motion: reduce)').matches) { details.close(); return; }
        details.classList.add('is-closing');
        window.setTimeout(() => details.close(), 240);
    }
    byId('myrCloseDetails').addEventListener('click', closeDetails);
    details.addEventListener('cancel', event => { if (!confirm.open) { event.preventDefault(); closeDetails(); } });
    details.addEventListener('click', event => {
        const r = details.getBoundingClientRect();
        if (event.target === details &&
            (event.clientX < r.left || event.clientX > r.right || event.clientY < r.top || event.clientY > r.bottom)) closeDetails();
    });
    details.addEventListener('close', () => {
        details.classList.remove('is-closing');
        syncScroll();
        if (!confirm.open && lastOpener?.isConnected) lastOpener.focus({ preventScroll: true });
    });

    cancelButton.addEventListener('click', () => openConfirm(current, null));
    keepButton.addEventListener('click', () => { if (!busy) confirm.close(); });
    confirm.addEventListener('cancel', event => { if (busy) event.preventDefault(); });
    confirm.addEventListener('close', () => {
        syncScroll();
        if (details.open && !cancelButton.hidden) cancelButton.focus({ preventScroll: true });
        else if (!details.open && lastOpener?.isConnected && !lastOpener.hidden) lastOpener.focus({ preventScroll: true });
    });

    byId('myrCancelForm').addEventListener('submit', async event => {
        event.preventDefault();
        if (busy || !current) return;
        refreshCancellationState();
        if (!canCancel(current)) {
            showError('Cancellation is unavailable after the scheduled reservation time.');
            return;
        }
        const target = current;
        const token = byId('myrTokenForm').querySelector('input[name="__RequestVerificationToken"]');
        if (!token) { showError('Your session could not be verified. Refresh the page and try again.'); return; }
        busy = true;
        confirmSubmit.disabled = true;
        keepButton.disabled = true;
        confirmSubmit.textContent = 'Cancelling…';
        confirm.setAttribute('aria-busy', 'true');
        cancelError.hidden = true;
        const data = new FormData();
        data.set('id', String(target.id));
        data.set('__RequestVerificationToken', token.value);
        try {
            const response = await fetch(cfg.cancelUrl, {
                method: 'POST', body: data, credentials: 'same-origin',
                headers: { 'Accept': 'application/json', 'X-Requested-With': 'XMLHttpRequest' }
            });
            if (response.redirected) { window.location.assign(response.url); return; }
            if (response.status === 401 || response.status === 403) {
                showError('Your session has ended or access is unavailable. Refresh the page and sign in again.'); return;
            }
            if (!(response.headers.get('content-type') || '').includes('application/json'))
                throw new Error('Unexpected response');
            const result = await response.json();
            if (!response.ok || !result.success) {
                showError(result.message || 'We could not cancel this reservation. Please refresh and check its latest status.');
                if (['too_late', 'status_changed', 'not_found'].includes(result.code)) {
                    target.canCancel = false;
                    target.cancelMessage = result.message;
                }
                return;
            }
            // Reload so the next reservation is promoted to the ticket and the count/limit are re-read from the server.
            // The success popup comes from the server (TempData["SuccessMessage"]) and shows after the reload.
            window.location.reload();
            return;
        } catch {
            showError('We could not confirm the result. Refresh the page to check this reservation before trying again.');
        } finally {
            busy = false;
            keepButton.disabled = false;
            confirmSubmit.textContent = 'Yes, cancel reservation';
            confirm.removeAttribute('aria-busy');
            refreshCancellationState();
            syncScroll();
        }
    });

    // Presentation only; POST checks the current time/status again on the server.
    syncQuickCancel();
    const timer = window.setInterval(refreshCancellationState, 15000);
    window.addEventListener('pagehide', () => window.clearInterval(timer));
})();