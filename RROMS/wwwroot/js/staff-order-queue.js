/* Owner: Member 3 / US25. No shared scripts or notification styles are replaced. */
(() => {
    'use strict';
    const root = document.querySelector('[data-staff-queue]');
    if (!root) return;
    const workspace = root.querySelector('[data-sq-workspace]');
    const drawer = root.querySelector('[data-sq-drawer]');
    const backdrop = root.querySelector('[data-sq-backdrop]');
    const base = new URL(root.dataset.baseUrl, location.href);
    const mobile = matchMedia('(max-width: 61.99rem)');
    const reduced = matchMedia('(prefers-reduced-motion: reduce)');
    const waitClocks = new WeakMap();
    const browserReload = performance.getEntriesByType('navigation')[0]?.type === 'reload';
    let searchEdited = false;
    function clearReloadDraft() {
        if (browserReload && !searchEdited) root.querySelector('#sq-search-input').value = '';
    }
    let selected = Number(drawer.querySelector('[data-sq-detail]')?.dataset.sqDetail || 0);
    let queueRequest, detailRequest, pollTimer, noticeWrap, dismissedStamp = '', pendingSignal;
    let accepting = false, updating = false, polling = false, stopped = false;
    let previousBodyOverflow = null;
    const panel = () => root.querySelector('[data-sq-panel]');
    let baselineStamp = panel().dataset.stamp;
    let baselineNewest = Number(panel().dataset.newestId);

    function prepareSortPicker() {
        const select = panel().querySelector('[data-sq-sort]');
        const picker = panel().querySelector('[data-sq-sort-picker]');
        if (!select || !picker) return;
        select.hidden = true;
        picker.hidden = false;
    }
    function closeSort(restoreFocus = false) {
        const picker = panel().querySelector('[data-sq-sort-picker]');
        if (!picker) return;
        picker.querySelector('[data-sq-sort-menu]').hidden = true;
        const trigger = picker.querySelector('[data-sq-sort-trigger]');
        trigger.setAttribute('aria-expanded', 'false');
        if (restoreFocus) trigger.focus({ preventScroll: true });
    }
    function openSort(edge = null) {
        const picker = panel().querySelector('[data-sq-sort-picker]');
        if (!picker || accepting || updating) return;
        picker.querySelector('[data-sq-sort-menu]').hidden = false;
        picker.querySelector('[data-sq-sort-trigger]').setAttribute('aria-expanded', 'true');
        const options = [...picker.querySelectorAll('[data-sq-sort-option]')];
        const selectedOption = options.find(option => option.getAttribute('aria-selected') === 'true');
        (edge === 'first' ? options[0] : edge === 'last' ? options.at(-1) : selectedOption || options[0])?.focus({ preventScroll: true });
    }

    function endpoint(handler, values = {}) {
        const url = new URL(base);
        if (handler) url.searchParams.set('handler', handler);
        for (const [key, value] of Object.entries(values))
            if (value !== null && value !== undefined && value !== '') url.searchParams.set(key, value);
        return url;
    }
    function viewValues() {
        return { search: panel().dataset.search, dining: panel().dataset.dining, pageNumber: panel().dataset.page, sort: panel().dataset.sort || 'newest', showAll: panel().dataset.showAll || 'false' };
    }
    function rememberUrl() {
        const url = endpoint('', { ...viewValues(), orderId: selected || null });
        history.replaceState(history.state, '', url);
    }
    async function request(url, options = {}) {
        const response = await fetch(url, {
            credentials: 'same-origin', cache: 'no-store', ...options,
            headers: { 'X-Requested-With': 'XMLHttpRequest', ...options.headers }
        });
        if (response.redirected || response.status === 401 || response.status === 403) {
            stopped = true;
            throw new Error('Your staff session needs attention. Refresh the page and sign in again.');
        }
        return response;
    }
    function announce(message) { root.querySelector('[data-sq-live]').textContent = message; }
    function removeNotice() { noticeWrap?.remove(); noticeWrap = null; }
    function notice(message, kind = 'info', action = false) {
        removeNotice();
        const wrap = document.createElement('div');
        wrap.className = 'yoru-toast-wrap sq-notice-wrap';
        const box = document.createElement('div');
        box.className = 'yoru-toast yoru-toast--' + kind + ' sq-notice';
        box.setAttribute('role', kind === 'error' ? 'alert' : 'status');
        box.innerHTML = '<span class="yoru-toast-seal" aria-hidden="true"><i class="bi"></i></span>'
            + '<div class="yoru-toast-body"><p class="yoru-toast-title"><span class="yoru-toast-jp"></span><span data-title></span></p><p class="yoru-toast-msg"></p></div>'
            + '<button type="button" class="yoru-toast-close" aria-label="Close notification"><i class="bi bi-x-lg"></i></button>';
        box.querySelector('.yoru-toast-seal i').classList.add(kind === 'success' ? 'bi-check-lg' : kind === 'error' ? 'bi-exclamation-lg' : 'bi-info-lg');
        box.querySelector('.yoru-toast-jp').textContent = kind === 'success' ? '完了' : kind === 'error' ? '注意' : 'お知らせ';
        box.querySelector('[data-title]').textContent = action
            ? (pendingSignal?.newestId > baselineNewest ? 'New orders received' : 'Order queue updated')
            : kind === 'success' ? 'All set' : kind === 'error' ? 'Oops' : 'Notice';
        box.querySelector('.yoru-toast-msg').textContent = message;
        const close = () => {
            if (action && pendingSignal) dismissedStamp = pendingSignal.stamp;
            box.classList.add('is-leaving');
            setTimeout(() => { wrap.remove(); if (noticeWrap === wrap) noticeWrap = null; }, reduced.matches ? 0 : 400);
        };
        box.querySelector('.yoru-toast-close').addEventListener('click', close);
        if (action) {
            const button = document.createElement('button'); button.type = 'button';
            button.className = 'sq-notice-action'; button.textContent = 'Update queue';
            button.addEventListener('click', () => refreshQueue(viewValues()));
            box.insertBefore(button, box.querySelector('.yoru-toast-close'));
        } else {
            const bar = document.createElement('span'); bar.className = 'yoru-toast-bar';
            bar.setAttribute('aria-hidden', 'true'); bar.addEventListener('animationend', close);
            box.append(bar);
            if (reduced.matches) setTimeout(close, 5000);
        }
        wrap.append(box); document.body.append(wrap); noticeWrap = wrap;
    }
    function waitLabel(minutes) {
        if (minutes < 1) return 'Just received';
        if (minutes < 60) return minutes + ' min';
        const hours = Math.floor(minutes / 60), remainder = minutes % 60;
        return hours + ' hr' + (remainder ? ' ' + remainder + ' min' : '');
    }
    function updateWaits() {
        const now = performance.now();
        root.querySelectorAll('[data-sq-wait]').forEach(element => {
            if (!waitClocks.has(element)) waitClocks.set(element, { seconds: Number(element.dataset.sqWait), at: now });
            const clock = waitClocks.get(element);
            const seconds = Math.max(0, clock.seconds + (now - clock.at) / 1000);
            const minutes = Math.floor(seconds / 60);
            element.querySelector('[data-sq-wait-label]').textContent = waitLabel(minutes);
            element.classList.toggle('sq-wait--normal', minutes < 10);
            element.classList.toggle('sq-wait--warn', minutes >= 10 && minutes < 20);
            element.classList.toggle('sq-wait--long', minutes >= 20);
            element.querySelector('[data-sq-long]').hidden = minutes < 20;
        });
    }
    function highlight() {
        root.querySelectorAll('[data-sq-row]').forEach(row => {
            const active = Number(row.dataset.sqRow) === selected;
            row.classList.toggle('is-selected', active);
            row.querySelector('[data-sq-view-label]').textContent = active ? 'Viewing' : 'View order';
            const link = row.querySelector('[data-sq-open]');
            if (active) link.setAttribute('aria-current', 'true'); else link.removeAttribute('aria-current');
        });
        const form = drawer.querySelector('[data-sq-accept]');
        if (form) for (const [name, value] of Object.entries(viewValues())) form.elements.namedItem(name).value = value;
    }
    function mobileMode() {
        const modal = mobile.matches && !drawer.hidden;
        backdrop.hidden = !modal;
        if (modal) {
            drawer.setAttribute('role', 'dialog'); drawer.setAttribute('aria-modal', 'true');
            drawer.setAttribute('aria-labelledby', 'sq-detail-title');
            if (previousBodyOverflow === null) { previousBodyOverflow = document.body.style.overflow; document.body.style.overflow = 'hidden'; }
            root.querySelectorAll('[data-sq-controls], [data-sq-board]').forEach(region => { region.inert = true; });
        } else {
            drawer.removeAttribute('role'); drawer.removeAttribute('aria-modal'); drawer.removeAttribute('aria-labelledby');
            if (previousBodyOverflow !== null) { document.body.style.overflow = previousBodyOverflow; previousBodyOverflow = null; }
            root.querySelectorAll('[data-sq-controls], [data-sq-board]').forEach(region => { region.inert = false; });
        }
    }
    function closeDrawer(focus = true) {
        if (accepting) return;
        detailRequest?.abort();
        const id = selected; selected = 0;
        drawer.hidden = true; drawer.replaceChildren(); workspace.classList.remove('is-open');
        mobileMode(); highlight(); rememberUrl();
        if (focus) (root.querySelector('[data-sq-open="' + id + '"]') || root.querySelector('#sq-queue-title'))?.focus({ preventScroll: true });
    }
    async function openDetails(id) {
        if (accepting) return;
        detailRequest?.abort(); const controller = new AbortController(); detailRequest = controller;
        let timedOut = false;
        const timeout = setTimeout(() => { timedOut = true; controller.abort(); }, 15000);
        try {
            const response = await request(endpoint('Details', { id }), { signal: controller.signal });
            if (!response.ok) throw new Error(response.status === 404 ? 'That order is no longer available. Update the queue.' : 'Could not open this order. Please try again.');
            const html = await response.text(); if (controller.signal.aborted) return;
            const template = document.createElement('template'); template.innerHTML = html;
            if (!template.content.querySelector('[data-sq-detail]')) throw new Error('Could not open this order. Please refresh the page.');
            selected = Number(id); drawer.replaceChildren(template.content); drawer.hidden = false;
            workspace.classList.add('is-open'); mobileMode(); highlight(); updateWaits(); rememberUrl();
            drawer.querySelector('#sq-detail-title').focus({ preventScroll: true });
            if (!mobile.matches && workspace.getBoundingClientRect().top < 85)
                workspace.scrollIntoView({ block: 'start', behavior: reduced.matches ? 'instant' : 'smooth' });
        } catch (error) {
            if (timedOut) notice('Opening this order timed out. Please try again.', 'error');
            else if (!controller.signal.aborted) notice(error.message, 'error');
        } finally { clearTimeout(timeout); }
    }
    async function refreshQueue(values, { announceResult = true } = {}) {
        if (accepting) return false;
        queueRequest?.abort(); const controller = new AbortController(); queueRequest = controller;
        updating = true; panel().setAttribute('aria-busy', 'true');
        const timeout = setTimeout(() => controller.abort(), 15000);
        try {
            const response = await request(endpoint('Queue', values), { signal: controller.signal });
            if (!response.ok) throw new Error('Could not update the queue. Your displayed orders are unchanged. Please try again.');
            const template = document.createElement('template'); template.innerHTML = await response.text();
            const next = template.content.querySelector('[data-sq-panel]');
            if (!next || controller.signal.aborted) { if (controller.signal.aborted) return false; throw new Error('Please refresh the page to continue.'); }
            const nextControls = next.querySelector('[data-sq-controls]');
            const nextBoard = next.querySelector('[data-sq-board]');
            if (!nextControls || !nextBoard) throw new Error('Please refresh the page to continue.');
            const scroll = { top: window.scrollY, left: window.scrollX, behavior: 'instant' };
            const hadFocus = panel().contains(document.activeElement) && !drawer.contains(document.activeElement);
            // Keep the workspace/drawer nodes intact, including their focus and open state.
            panel().querySelector('[data-sq-controls]').replaceWith(nextControls);
            workspace.querySelector('[data-sq-board]').replaceWith(nextBoard);
            Object.assign(panel().dataset, next.dataset);
            prepareSortPicker();
            mobileMode();
            for (const key of ['pending', 'dineIn', 'takeaway']) root.querySelector('[data-sq-count="' + key + '"]').textContent = next.dataset[key];
            baselineStamp = next.dataset.stamp; baselineNewest = Number(next.dataset.newestId);
            pendingSignal = null; dismissedStamp = ''; removeNotice();
            root.querySelector('[data-sq-connection]').hidden = true;
            highlight(); updateWaits(); rememberUrl();
            if (hadFocus) panel().querySelector('#sq-queue-title').focus({ preventScroll: true });
            window.scrollTo(scroll);
            if (announceResult) announce('Order queue updated. ' + next.dataset.pending + ' pending orders.');
            return true;
        } catch (error) {
            if (queueRequest === controller) notice(controller.signal.aborted ? 'The update timed out. Your displayed orders are unchanged. Please retry.' : error.message, 'error');
            return false;
        } finally {
            clearTimeout(timeout);
            if (queueRequest === controller) { updating = false; panel().removeAttribute('aria-busy'); }
        }
    }
    function setUpdateAvailable(available) {
        const button = root.querySelector('[data-sq-refresh]');
        button.querySelector('[data-sq-update]').textContent = available ? 'Update queue' : 'Refresh queue';
        button.classList.toggle('is-pending', available);
        button.setAttribute('aria-label', available ? 'Updates available. Update order queue' : 'Refresh order queue');
        button.title = available ? 'Updates available — update queue' : 'Refresh queue';
    }
    async function checkUpdates() {
        if (polling || stopped || document.hidden || accepting || updating) return;
        polling = true; const controller = new AbortController();
        const checkedStamp = baselineStamp;
        const timeout = setTimeout(() => controller.abort(), 10000);
        try {
            const response = await request(endpoint('Signal'), { signal: controller.signal });
            if (!response.ok) throw new Error('Live check unavailable');
            const signal = await response.json();
            if (updating || accepting || baselineStamp !== checkedStamp) return;
            root.querySelector('[data-sq-connection]').hidden = true;
            if (signal.stamp !== baselineStamp) {
                pendingSignal = signal;
                setUpdateAvailable(true);
                if (signal.stamp !== dismissedStamp && !document.querySelector('.yoru-toast-wrap'))
                    notice('Updates are ready. Refresh the queue when you’re ready to review them.', 'info', true);
            } else {
                pendingSignal = null; setUpdateAvailable(false);
            }
        } catch {
            root.querySelector('[data-sq-connection]').hidden = false;
        } finally { clearTimeout(timeout); polling = false; }
    }
    async function acceptOrder(form) {
        if (accepting || updating) return;
        accepting = true;
        const button = form.querySelector('button[type="submit"]');
        const label = button.textContent; button.disabled = true; button.textContent = 'Accepting…'; form.setAttribute('aria-busy', 'true');
        const controller = new AbortController(); const timeout = setTimeout(() => controller.abort(), 20000);
        try {
            const response = await request(form.action, { method: 'POST', body: new FormData(form), signal: controller.signal });
            const result = await response.json();
            accepting = false;
            if (response.ok && result.success) {
                closeDrawer(false);
                const refreshed = await refreshQueue(viewValues(), { announceResult: false });
                notice(result.message + (refreshed ? '' : ' Refresh the queue to see the latest list.'), 'success');
                root.querySelector('#sq-queue-title').focus({ preventScroll: true });
            } else if ([404, 409].includes(response.status)) {
                closeDrawer(false); await refreshQueue(viewValues(), { announceResult: false }); notice(result.message, 'info');
            } else {
                notice(result.message || 'Could not confirm this order. Update the queue before trying again.', 'error');
                if (result.refresh) checkUpdates();
            }
        } catch {
            notice('Could not confirm the result. Update the queue and check this order before trying again.', 'error');
        } finally {
            clearTimeout(timeout); accepting = false;
            if (button.isConnected) { button.disabled = false; button.textContent = label; form.removeAttribute('aria-busy'); }
        }
    }
    root.addEventListener('click', event => {
        const sortOption = event.target.closest('[data-sq-sort-option]');
        if (sortOption) {
            const select = panel().querySelector('[data-sq-sort]');
            closeSort(true);
            if (select.value !== sortOption.dataset.sqSortOption) {
                select.value = sortOption.dataset.sqSortOption;
                select.dispatchEvent(new Event('change', { bubbles: true }));
            }
            return;
        }
        const sortTrigger = event.target.closest('[data-sq-sort-trigger]');
        if (sortTrigger) {
            if (sortTrigger.getAttribute('aria-expanded') === 'true') closeSort(); else openSort();
            return;
        }
        const link = event.target.closest('[data-sq-open]')
            || (window.getSelection()?.toString() ? null : event.target.closest('[data-sq-ticket]')?.querySelector('[data-sq-open]'));
        if (link && !event.ctrlKey && !event.metaKey && !event.shiftKey && !event.altKey && event.button === 0) { event.preventDefault(); openDetails(link.dataset.sqOpen); return; }
        const close = event.target.closest('[data-sq-close], [data-sq-backdrop]');
        if (close) { event.preventDefault(); closeDrawer(); return; }
        const navigate = event.target.closest('[data-sq-filter], [data-sq-page]');
        if (navigate && !event.ctrlKey && !event.metaKey && !event.shiftKey && !event.altKey && event.button === 0) {
            event.preventDefault(); const params = new URL(navigate.href).searchParams;
            refreshQueue(Object.fromEntries(params)); return;
        }
        if (event.target.closest('[data-sq-refresh], [data-sq-update]')) refreshQueue(viewValues());
        if (event.target.closest('[data-sq-retry]')) { stopped = false; checkUpdates(); schedulePoll(); }
    });
    root.addEventListener('submit', event => {
        if (event.target.matches('[data-sq-search]')) {
            event.preventDefault(); const form = event.target;
            const input = form.elements.search;
            if (!input.value.trim()) { input.setCustomValidity('Enter an order number or customer name to search.'); input.reportValidity(); input.focus(); return; }
            input.setCustomValidity('');
            refreshQueue({ search: input.value.trim(), dining: form.elements.dining.value, sort: panel().dataset.sort, showAll: panel().dataset.showAll || 'false' });
        } else if (event.target.matches('[data-sq-accept]')) { event.preventDefault(); acceptOrder(event.target); }
    });
    root.addEventListener('change', async event => {
        if (!event.target.matches('[data-sq-sort]')) return;
        const select = event.target;
        const changed = await refreshQueue({ ...viewValues(), sort: select.value, pageNumber: 1 });
        if (!changed && select.isConnected) select.value = panel().dataset.sort || 'newest';
    });
    root.addEventListener('input', event => {
        if (event.target.matches('#sq-search-input')) { searchEdited = true; event.target.setCustomValidity(''); }
    });
    document.addEventListener('pointerdown', () => root.classList.remove('sq-keyboard-focus'), true);
    document.addEventListener('click', event => { if (!event.target.closest('[data-sq-sort-picker]')) closeSort(); });
    root.addEventListener('keydown', event => {
        const picker = event.target.closest('[data-sq-sort-picker]');
        if (!picker) return;
        const trigger = event.target.closest('[data-sq-sort-trigger]');
        if (trigger && ['ArrowDown', 'ArrowUp', 'Enter', ' '].includes(event.key)) {
            event.preventDefault(); event.stopPropagation();
            openSort(event.key === 'ArrowUp' ? 'last' : event.key === 'ArrowDown' ? 'first' : null);
            return;
        }
        const menu = picker.querySelector('[data-sq-sort-menu]');
        if (menu.hidden) return;
        if (event.key === 'Tab') { closeSort(true); return; }
        if (!['ArrowDown', 'ArrowUp', 'Home', 'End', 'Enter', ' ', 'Escape'].includes(event.key)) return;
        event.preventDefault(); event.stopPropagation();
        if (event.key === 'Escape') { closeSort(true); return; }
        const options = [...picker.querySelectorAll('[data-sq-sort-option]')];
        const index = options.indexOf(document.activeElement);
        if (event.key === 'Enter' || event.key === ' ') { options[index]?.click(); return; }
        const next = event.key === 'Home' ? 0 : event.key === 'End' ? options.length - 1
            : (index + (event.key === 'ArrowDown' ? 1 : -1) + options.length) % options.length;
        options[next].focus({ preventScroll: true });
    });
    root.addEventListener('error', event => { if (event.target.matches('.sq-dish__photo img')) event.target.hidden = true; }, true);
    document.addEventListener('keydown', event => {
        if (event.key === 'Tab') root.classList.add('sq-keyboard-focus');
        if (event.key === 'Escape' && !panel().querySelector('[data-sq-sort-menu]').hidden) { event.preventDefault(); closeSort(true); return; }
        if (drawer.hidden) return;
        if (event.key === 'Escape') { event.preventDefault(); closeDrawer(); }
        if (event.key === 'Tab' && mobile.matches) {
            const focusable = [...drawer.querySelectorAll('a[href], button:not(:disabled), input:not([type="hidden"]):not(:disabled), [tabindex="0"]')];
            const first = focusable[0], last = focusable.at(-1);
            if (!first) return;
            if (event.shiftKey && (document.activeElement === first || !focusable.includes(document.activeElement))) { event.preventDefault(); last.focus(); }
            else if (!event.shiftKey && (document.activeElement === last || !focusable.includes(document.activeElement))) { event.preventDefault(); first.focus(); }
        }
    });
    function schedulePoll() { clearTimeout(pollTimer); if (!stopped) pollTimer = setTimeout(async () => { await checkUpdates(); schedulePoll(); }, 15000); }
    document.addEventListener('visibilitychange', () => { if (!document.hidden) { updateWaits(); checkUpdates(); schedulePoll(); } });
    addEventListener('pagehide', () => { clearTimeout(pollTimer); queueRequest?.abort(); detailRequest?.abort(); removeNotice(); });
    addEventListener('pageshow', () => { clearReloadDraft(); updateWaits(); schedulePoll(); });
    mobile.addEventListener('change', mobileMode);
    root.classList.add('is-enhanced'); prepareSortPicker(); clearReloadDraft(); highlight(); mobileMode(); updateWaits();
    setInterval(updateWaits, 15000); schedulePoll();
})();
