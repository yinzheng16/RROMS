// Owner: Member 2 - US18. Upcoming / History tabs, history filter and paging, and the live status refresh.
// Everything the server sends is written with textContent, never as HTML.
(() => {
    'use strict';
    const page = document.getElementById('myReservations');
    if (!page) return;
    const wrap = page.querySelector('.myr-wrap');
    const tabUp = document.getElementById('myrListHeading');
    const tabHist = document.getElementById('myrHistoryTab');
    const panelHist = document.getElementById('myrHistory');
    const reduce = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;

    // close every opened "Steps" panel, so the list is back to how it first looked
    function collapseSteps() {
        panelHist.querySelectorAll('.myr-hist-toggle[aria-expanded="true"]').forEach(btn => {
            btn.setAttribute('aria-expanded', 'false');
            const box = document.getElementById(btn.getAttribute('aria-controls'));
            if (box) box.hidden = true;
        });
    }

    /* ---------- tabs ---------- */
    function showTab(name, focus) {
        const history = name === 'history';
        collapseSteps();
        wrap.classList.toggle('is-history', history);
        panelHist.hidden = !history;
        tabUp.classList.toggle('is-on', !history);
        tabHist.classList.toggle('is-on', history);
        tabUp.setAttribute('aria-selected', String(!history));
        tabHist.setAttribute('aria-selected', String(history));
        tabUp.tabIndex = history ? -1 : 0;
        tabHist.tabIndex = history ? 0 : -1;
        if (focus) (history ? tabHist : tabUp).focus();
        try {
            const url = new URL(window.location.href);
            url.hash = history ? '#history' : '';
            window.history.replaceState(null, '', url.pathname + url.search + url.hash);
        } catch (e) { /* the page still works without the address change */ }
    }
    tabUp.addEventListener('click', () => showTab('upcoming'));
    tabHist.addEventListener('click', () => showTab('history'));
    [tabUp, tabHist].forEach(tab => tab.addEventListener('keydown', event => {
        if (event.key === 'ArrowRight' || event.key === 'ArrowLeft' || event.key === 'Home' || event.key === 'End') {
            event.preventDefault();
            showTab(event.key === 'ArrowLeft' || event.key === 'Home' ? 'upcoming' : 'history', true);
        }
    }));
    if (window.location.hash === '#history') showTab('history');

    /* ---------- history: filter, 5 per page, steps ---------- */
    const list = document.getElementById('myrHistList');
    if (list) {
        const PAGE_SIZE = 5;
        const rows = [...list.querySelectorAll(':scope > .myr-hist')];
        const none = document.getElementById('myrHistNone');
        const chips = [...panelHist.querySelectorAll('.myr-chip')];
        let filter = 'all';
        let pageNo = 1;
        let showAll = false;

        const nav = document.createElement('nav');
        nav.className = 'myr-pager';
        nav.setAttribute('aria-label', 'History pages');
        const info = document.createElement('span');
        info.className = 'myr-pager-info';
        info.setAttribute('aria-live', 'polite');
        const numbers = document.createElement('div');
        numbers.className = 'myr-pager-pages';
        const toggle = document.createElement('button');
        toggle.type = 'button';
        toggle.className = 'myr-pager-all';
        nav.append(info, numbers, toggle);
        list.after(nav);

        const arrow = (dir, label, target, pages) => {
            const b = document.createElement('button');
            b.type = 'button';
            b.className = 'myr-pager-step';
            const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
            svg.setAttribute('viewBox', '0 0 24 24');
            svg.setAttribute('aria-hidden', 'true');
            const path = document.createElementNS('http://www.w3.org/2000/svg', 'path');
            path.setAttribute('d', dir === 'prev' ? 'm15 6-6 6 6 6' : 'm9 6 6 6-6 6');
            svg.append(path);
            b.append(svg);
            b.setAttribute('aria-label', label);
            b.disabled = target < 1 || target > pages;
            b.dataset.page = String(target);
            return b;
        };

        function render() {
            const shown = rows.filter(r => filter === 'all' || r.dataset.status === filter);
            const pages = Math.max(1, Math.ceil(shown.length / PAGE_SIZE));
            if (pageNo > pages) pageNo = pages;
            const from = showAll ? 0 : (pageNo - 1) * PAGE_SIZE;
            const to = showAll ? shown.length : Math.min(shown.length, from + PAGE_SIZE);

            rows.forEach(r => {
                const match = filter === 'all' || r.dataset.status === filter;
                const idx = shown.indexOf(r);
                r.classList.toggle('is-filtered-out', !match);
                r.classList.toggle('is-paged-out', match && (idx < from || idx >= to));
            });
            none.hidden = shown.length > 0;
            nav.hidden = shown.length <= PAGE_SIZE;
            info.textContent = showAll
                ? 'Showing all ' + shown.length
                : 'Showing ' + (from + 1) + '–' + to + ' of ' + shown.length;
            numbers.hidden = showAll;
            numbers.replaceChildren();
            if (!showAll) {
                numbers.append(arrow('prev', 'Previous page', pageNo - 1, pages));
                for (let n = 1; n <= pages; n++) {
                    const b = document.createElement('button');
                    b.type = 'button';
                    b.className = 'myr-pager-num' + (n === pageNo ? ' is-on' : '');
                    b.textContent = String(n);
                    b.dataset.page = String(n);
                    b.setAttribute('aria-label', 'Page ' + n);
                    if (n === pageNo) b.setAttribute('aria-current', 'page');
                    numbers.append(b);
                }
                numbers.append(arrow('next', 'Next page', pageNo + 1, pages));
            }
            toggle.textContent = showAll ? 'Show less' : 'Show all';
            toggle.setAttribute('aria-pressed', String(showAll));
        }

        numbers.addEventListener('click', event => {
            const b = event.target.closest('button[data-page]');
            if (!b || b.disabled) return;
            pageNo = Number(b.dataset.page);
            collapseSteps();
            render();
            numbers.querySelector('[aria-current="page"]')?.focus({ preventScroll: true });
        });
        toggle.addEventListener('click', () => { showAll = !showAll; pageNo = 1; collapseSteps(); render(); });

        panelHist.querySelector('.myr-filters').addEventListener('click', event => {
            const chip = event.target.closest('.myr-chip');
            if (!chip) return;
            filter = chip.dataset.filter;
            pageNo = 1;
            collapseSteps();
            chips.forEach(c => {
                c.classList.toggle('is-on', c === chip);
                c.setAttribute('aria-pressed', String(c === chip));
            });
            render();
        });

        // "Steps" opens the timeline of one booking
        // (a click anywhere on the card does the same; links, buttons and the opened part are left alone)
        list.addEventListener('click', event => {
            let btn = event.target.closest('.myr-hist-toggle');
            if (!btn) {
                if (event.target.closest('button, a, input, .myr-hist-more')) return;
                if (window.getSelection && String(window.getSelection()).length > 0) return;   // the guest is copying text
                const card = event.target.closest('.myr-hist');
                btn = card && card.querySelector('.myr-hist-toggle');
                if (!btn) return;
            }
            const box = document.getElementById(btn.getAttribute('aria-controls'));
            if (!box) return;
            const open = box.hidden;
            box.hidden = !open;
            btn.setAttribute('aria-expanded', String(open));
        });

        render();
    }

    /* ---------- live status ---------- */
    const url = page.dataset.statusesUrl;
    const note = document.getElementById('myrLiveNote');
    const noteText = document.getElementById('myrLiveText');
    const noteBtn = document.getElementById('myrLiveReload');
    const STEPS = ['Pending', 'Confirmed', 'Seated', 'Completed'];
    const ICON = { Pending: '#myr-hourglass', Confirmed: '#myr-check', Seated: '#myr-table', Completed: '#myr-check', Cancelled: '#myr-cross', NoShow: '#myr-clock' };
    const CLASS = { Pending: 'is-pending', Confirmed: 'is-confirmed', Seated: 'is-seated', Completed: 'is-completed', Cancelled: 'is-cancelled', NoShow: 'is-noshow' };
    const LABEL = s => (s === 'NoShow' ? 'No-show' : s);
    const STILL_UPCOMING = ['Pending', 'Confirmed', 'Seated'];

    if (!url) return;
    if (noteBtn) noteBtn.addEventListener('click', () => window.location.reload());

    function paintBadge(card, status) {
        const badge = card.querySelector('[data-live-badge]');
        if (badge) {
            Object.values(CLASS).forEach(c => badge.classList.remove(c));
            badge.classList.add(CLASS[status] || 'is-pending');
            const use = badge.querySelector('use');
            if (use) use.setAttribute('href', ICON[status] || '#myr-hourglass');
            const text = badge.querySelector('span');
            if (text) text.textContent = LABEL(status);
            badge.classList.remove('is-flash');
            void badge.offsetWidth;
            if (!reduce) badge.classList.add('is-flash');
        }
        const track = card.querySelector('.myr-track');
        if (track && STEPS.includes(status)) {
            const at = STEPS.indexOf(status);
            [...track.children].forEach((li, i) => {
                li.className = i < at ? 'is-done' : i === at ? 'is-now' : '';
            });
            track.dataset.track = status;
        }
        card.dataset.status = status;
        // the "Next up" card: pill and note follow the status
        const pill = card.closest('.myr-next')?.querySelector('.myr-next-pill');
        if (pill) pill.textContent = status === 'Seated' ? 'NOW SEATED' : 'NEXT UP';
        const line = card.querySelector('[data-live-note]');
        if (line && status === 'Seated') line.textContent = 'You are being served. Enjoy your evening.';
    }

    function say(message) {
        noteText.textContent = message;
        note.hidden = false;
    }

    let timer = null;
    let failures = 0;
    let stopped = false;

    async function poll() {
        if (stopped || document.hidden) return;
        try {
            const response = await fetch(url, { headers: { Accept: 'application/json' }, credentials: 'same-origin', cache: 'no-store' });
            if (response.status === 401 || response.status === 403 || response.redirected) { stopped = true; return; }
            if (!response.ok) throw new Error(String(response.status));
            const data = await response.json();
            failures = 0;

            const seen = new Map(data.items.map(x => [Number(x.id), x]));
            const changes = [];
            let needsReload = false;
            page.querySelectorAll('[data-reservation-id][data-status]').forEach(card => {
                if (card.classList.contains('myr-hist')) return;           // finished bookings no longer change
                const now = seen.get(Number(card.dataset.reservationId));
                if (!now || now.status === card.dataset.status) return;

                const ref = (card.getAttribute('aria-label') || '').replace('Reservation ', '');
                changes.push(ref + ' is now ' + LABEL(now.status));
                if (STILL_UPCOMING.includes(now.status)) {
                    paintBadge(card, now.status);
                } else {
                    card.dataset.status = now.status;                       // do not announce it twice
                    needsReload = true;
                }
            });
            if (changes.length === 1) say(changes[0] + (needsReload ? '. Refresh to see it in History.' : '.'));
            else if (changes.length > 1) say(changes.length + ' of your bookings have changed.' + (needsReload ? ' Refresh to update your lists.' : ''));
        } catch (e) {
            failures++;
        }
    }

    function schedule() {
        clearInterval(timer);
        // every 20 seconds, slower after repeated failures
        timer = setInterval(poll, failures > 2 ? 60000 : 20000);
    }
    schedule();
    document.addEventListener('visibilitychange', () => { if (!document.hidden) poll(); });
})();
