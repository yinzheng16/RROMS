/* Owner: Member 2 — US16 staff "Today's reservations".
   Filtering and search run in the browser on the day that is already loaded.
   Server calls: ?handler=Board (refresh), ?handler=Details (pop-up), ?handler=Confirm (POST). */
(() => {
    'use strict';

    const root = document.getElementById('sr');
    if (!root) return;

    const pageUrl = root.dataset.pageUrl || location.pathname;
    const date = root.dataset.date;
    const isToday = root.dataset.today === 'true';
    const menu = document.getElementById('srMenu');
    const modal = document.getElementById('srModal');
    const modalBox = document.getElementById('srModalBox');
    const toast = document.getElementById('srToast');
    const search = document.getElementById('srSearch');
    const refreshBtn = document.getElementById('srRefresh');
    const token = () => root.querySelector('input[name="__RequestVerificationToken"]')?.value || '';

    const state = { filter: 'all', q: '', area: new URLSearchParams(location.search).get('area') || '', view: root.dataset.view || 'ledger', busy: false, lastFocus: null };
    const FILTERS = ['all', 'pending', 'confirmed', 'seated', 'late', 'completed', 'cancelled'];

    const esc = s => String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    const icon = d => `<svg viewBox="0 0 24 24" aria-hidden="true" focusable="false">${d}</svg>`;
    const ICONS = {
        check: '<path d="m5 12.5 4.2 4.2L19 7"/>',
        eye: '<path d="M2 12s3.6-7 10-7 10 7 10 7-3.6 7-10 7S2 12 2 12Z"/><circle cx="12" cy="12" r="3"/>',
        phone: '<path d="M5 4h4l2 5-2.5 1.5a11 11 0 0 0 5 5L15 13l5 2v4a2 2 0 0 1-2 2A16 16 0 0 1 3 6a2 2 0 0 1 2-2Z"/>',
        copy: '<rect x="8" y="8" width="12" height="12" rx="2"/><path d="M16 8V6a2 2 0 0 0-2-2H6a2 2 0 0 0-2 2v8a2 2 0 0 0 2 2h2"/>',
        seat: '<path d="M6 20V9a3 3 0 0 1 3-3h6a3 3 0 0 1 3 3v11M4 14h16M8 20v-3m8 3v-3"/>',
        noshow: '<circle cx="12" cy="12" r="8.5"/><path d="m6 6 12 12"/>',
        x: '<path d="M6 6l12 12M18 6 6 18"/>'
    };

    // ---------------------------------------------------------------- filters + search
    const norm = s => (s || '').toLowerCase().trim();

    function matches(el) {
        const f = state.filter;
        if (f === 'late' ? el.dataset.late !== '1' : (f !== 'all' && el.dataset.bucket !== f)) return false;
        if (!state.q) return true;
        const hay = el.dataset.search || '';
        return state.q.split(/\s+/).every(t => hay.includes(t));
    }

    function applyFilters() {
        const rows = root.querySelectorAll('.sr-row');
        let shown = 0;
        rows.forEach(r => { const ok = matches(r); r.hidden = !ok; if (ok) shown++; });
        root.querySelectorAll('.tl-bar').forEach(b => { b.hidden = !matches(b); });

        // hide an hour heading when none of its bookings is visible
        root.querySelectorAll('.sr-hour').forEach(h => {
            let n = h.nextElementSibling, any = false;
            while (n && !n.classList.contains('sr-hour')) {
                if (n.classList.contains('sr-row') && !n.hidden) { any = true; break; }
                n = n.nextElementSibling;
            }
            h.hidden = !any;
        });
        applyArea();
        const empty = root.querySelector('.sr-nomatch');
        if (empty) empty.hidden = !(rows.length && shown === 0);

        root.querySelectorAll('.sr-tab').forEach(t => {
            const on = t.dataset.filter === state.filter;
            t.classList.toggle('is-on', on);
            t.setAttribute('aria-pressed', on ? 'true' : 'false');
        });
        syncUrl();
    }

    // timeline: show one dining area at a time
    function applyArea() {
        const chips = root.querySelectorAll('[data-area-filter]');
        if (!chips.length) return;
        if (state.area && ![...chips].some(c => c.dataset.areaFilter === state.area)) state.area = '';   // area not on this day
        chips.forEach(c => {
            const on = c.dataset.areaFilter === state.area;
            c.classList.toggle('is-on', on);
            c.setAttribute('aria-pressed', on ? 'true' : 'false');
        });
        root.querySelectorAll('.tl-area, .tl-lane').forEach(el => { el.hidden = !!state.area && el.dataset.area !== state.area; });
    }

    function setArea(a) { state.area = a || ''; applyArea(); syncUrl(); }

    function updateCounts() {
        const s = root.querySelector('[data-part="summary"]');
        if (!s) return;
        FILTERS.forEach(k => {
            const out = root.querySelector(`[data-count="${k}"]`);
            if (out && s.dataset['c' + k[0].toUpperCase() + k.slice(1)] !== undefined)
                out.textContent = s.dataset['c' + k[0].toUpperCase() + k.slice(1)];
        });
    }

    function setFilter(f) { state.filter = FILTERS.includes(f) ? f : 'all'; applyFilters(); }

    // after picking a tab, glide down so the list is in view
    function scrollToList() {
        const bar = root.querySelector('.sr-toolbar');
        if (!bar) return;
        const calm = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
        const behavior = calm ? 'auto' : 'smooth';

        // the booking this tab is about: the first one on show. "All" has no single booking, so it only glides to the list.
        let target = null;
        if (state.filter !== 'all') {
            target = state.view === 'timeline'
                ? [...root.querySelectorAll('.tl-bar')].find(b => !b.hidden && b.offsetParent)
                : root.querySelector('.sr-row:not([hidden])');
        }

        // where the toolbar sits when we just glide down to the list
        let top = bar.getBoundingClientRect().top + window.scrollY - 118;
        if (target) {
            const abs = target.getBoundingClientRect().top + window.scrollY;
            if (abs - top > window.innerHeight - 200) top = abs - window.innerHeight * 0.4;   // far down: bring it up
            // timeline scrolls sideways on small screens: bring the bar into view too
            const box = root.querySelector('.sr-tl');
            if (state.view === 'timeline' && box && box.scrollWidth > box.clientWidth) {
                const dx = target.getBoundingClientRect().left - box.getBoundingClientRect().left - 220;
                box.scrollBy({ left: dx, behavior });
            }
            target.classList.remove('is-ping'); void target.offsetWidth; target.classList.add('is-ping');
            setTimeout(() => target.classList.remove('is-ping'), 1700);
        }
        if (Math.abs(window.scrollY - top) < 8) return;
        window.scrollTo({ top: Math.max(0, top), behavior });
    }

    function clearFilters() { state.filter = 'all'; state.q = ''; if (search) search.value = ''; applyFilters(); }

    // ---------------------------------------------------------------- address bar (shareable view)
    function syncUrl() {
        const u = new URL(location.href);
        const set = (k, v) => v ? u.searchParams.set(k, v) : u.searchParams.delete(k);
        set('status', state.filter !== 'all' ? state.filter : '');
        set('q', state.q);
        set('view', state.view === 'timeline' ? 'timeline' : '');
        set('area', state.view === 'timeline' ? state.area : '');
        history.replaceState(null, '', u);
        // day arrows keep the chosen view
        root.querySelectorAll('[data-nav]').forEach(a => {
            const t = new URL(pageUrl, location.origin);
            t.searchParams.set('date', a.dataset.nav);
            if (state.view === 'timeline') { t.searchParams.set('view', 'timeline'); if (state.area) t.searchParams.set('area', state.area); }
            a.setAttribute('href', t.pathname + t.search);
        });
    }

    function setView(v) {
        state.view = v === 'timeline' ? 'timeline' : 'ledger';
        root.dataset.view = state.view;
        root.querySelectorAll('.sr-seg button').forEach(b => {
            const on = b.dataset.view === state.view;
            b.classList.toggle('is-on', on);
            b.setAttribute('aria-pressed', on ? 'true' : 'false');
        });
        syncUrl();
    }

    // ---------------------------------------------------------------- messages
    let toastTimer;
    function say(msg, bad) {
        toast.textContent = msg;
        toast.classList.toggle('is-error', !!bad);
        toast.hidden = false;
        clearTimeout(toastTimer);
        toastTimer = setTimeout(() => { toast.hidden = true; }, bad ? 6500 : 3800);
    }

    // ---------------------------------------------------------------- server calls
    async function refreshBoard(quiet) {
        if (state.busy) return;
        state.busy = true;
        const began = Date.now();
        if (!quiet) refreshBtn?.classList.add('is-spin');
        try {
            const res = await fetch(`${pageUrl}?handler=Board&date=${encodeURIComponent(date)}`, {
                credentials: 'same-origin', headers: { 'X-Requested-With': 'XMLHttpRequest' }, cache: 'no-store'
            });
            if (!res.ok) throw new Error(String(res.status));
            const doc = new DOMParser().parseFromString(await res.text(), 'text/html');
            ['summary', 'lists'].forEach(p => {
                const fresh = doc.querySelector(`[data-part="${p}"]`);
                const old = root.querySelector(`[data-part="${p}"]`);
                if (fresh && old) old.replaceWith(fresh);
            });
            updateCounts();
            applyFilters();
            if (!quiet) {
                // let the spin be seen, then say it worked
                await new Promise(r => setTimeout(r, Math.max(0, 700 - (Date.now() - began))));
                say('Up to date · ' + new Date().toLocaleTimeString('en-US', { hour: 'numeric', minute: '2-digit' }));
            }
        } catch {
            if (!quiet) say('Could not refresh. Check your connection and try again.', true);
        } finally {
            state.busy = false;
            refreshBtn?.classList.remove('is-spin');
        }
    }

    async function confirmBooking(id, btn) {
        if (!id) return;
        const buttons = root.querySelectorAll(`[data-act="confirm"][data-id="${id}"]`);
        buttons.forEach(b => { b.disabled = true; });
        closeMenu();
        try {
            const res = await fetch(`${pageUrl}?handler=Confirm&id=${encodeURIComponent(id)}`, {
                method: 'POST', credentials: 'same-origin',
                headers: { 'X-Requested-With': 'XMLHttpRequest', 'RequestVerificationToken': token() }
            });
            let data = {};
            try { data = await res.json(); } catch { /* not JSON: handled below */ }
            if (res.ok && data.success) say(data.message || 'Reservation confirmed.');
            else say(data.message || 'Could not confirm this reservation. Refresh and try again.', true);
        } catch {
            say('We could not reach the server. Refresh and check this booking before trying again.', true);
        }
        await refreshBoard(true);
        if (!modal.hidden && modalBox.querySelector('.dv')?.dataset.id === String(id)) await openDetails(id, true);
        buttons.forEach(b => { b.disabled = false; });
    }

    // ---------------------------------------------------------------- "..." menu
    function closeMenu() {
        if (menu.hidden) return;
        menu.hidden = true;
        root.querySelectorAll('.sr-more[aria-expanded="true"]').forEach(b => b.setAttribute('aria-expanded', 'false'));
    }

    function openMenu(btn) {
        const host = btn.closest('[data-id]');
        if (!host) return;
        if (!menu.hidden && btn.getAttribute('aria-expanded') === 'true') { closeMenu(); return; }
        closeMenu();

        const d = host.dataset;
        const canConfirm = d.confirm === '1';
        const phone = (d.phone || '').trim();
        const initial = esc((d.name || '?').trim().charAt(0).toUpperCase());
        const meta = [d.ref, d.time, `${d.guests} guest${d.guests === '1' ? '' : 's'}`, d.table].filter(Boolean).join(' · ');

        menu.innerHTML =
            `<div class="m-head"><div class="av">${initial}</div><div><b>${esc(d.name)}</b><small>${esc(meta)}</small></div></div>` +
            (canConfirm
                ? `<button type="button" class="m-primary" data-act="confirm" data-id="${esc(d.id)}">${icon(ICONS.check)}<span><b>Confirm reservation</b><small>Keeps table ${esc(d.table || '')} and notifies ${esc((d.name || '').split(' ')[0])}</small></span></button>`
                : '') +
            `<div class="m-group" role="none">` +
            `<button type="button" class="m-item" role="menuitem" data-act="open" data-id="${esc(d.id)}">${icon(ICONS.eye)}<span>View details</span></button>` +
            `<button type="button" class="m-item" role="menuitem" data-act="call" data-phone="${esc(phone)}"${phone ? '' : ' disabled'}>${icon(ICONS.phone)}<span>Call guest</span><em>${esc(phone)}</em></button>` +
            `<button type="button" class="m-item" role="menuitem" data-copy="${esc(d.ref)}">${icon(ICONS.copy)}<span>Copy reference</span><em>${esc(d.ref)}</em></button>` +
            `</div>` +
            `<div class="m-sep">COMING NEXT</div>` +
            `<button type="button" class="m-item" role="menuitem" disabled>${icon(ICONS.seat)}<span>Mark as seated</span><em>soon</em></button>` +
            `<button type="button" class="m-item" role="menuitem" disabled>${icon(ICONS.noshow)}<span>Mark as no-show</span><em>soon</em></button>` +
            `<button type="button" class="m-item" role="menuitem" disabled>${icon(ICONS.x)}<span>Cancel booking</span><em>soon</em></button>`;

        // place it under the button; flip above when the screen bottom is too close
        menu.hidden = false;
        const host2 = root.getBoundingClientRect();
        const r = btn.getBoundingClientRect();
        const h = menu.offsetHeight, w = menu.offsetWidth;
        const below = window.innerHeight - r.bottom > h + 16 || r.top < h + 16;
        menu.dataset.place = below ? 'below' : 'above';
        const left = Math.max(8, r.left + r.width / 2 - w + 28) - host2.left;
        const top = (below ? r.bottom + 12 : r.top - h - 12) - host2.top;
        menu.style.left = left + 'px';
        menu.style.top = top + 'px';
        // the arrow follows the button even when the menu had to move sideways
        menu.style.setProperty('--arrow', Math.max(14, Math.min(w - 30, r.left + r.width / 2 - (left + host2.left) - 8)) + 'px');
        btn.setAttribute('aria-expanded', 'true');
        (menu.querySelector('.m-primary, .m-item:not(:disabled)'))?.focus();
    }

    // ---------------------------------------------------------------- details pop-up
    async function openDetails(id, keepFocus) {
        if (!keepFocus) state.lastFocus = document.activeElement;
        closeMenu();
        modal.hidden = false;
        document.body.style.overflow = 'hidden';
        if (!keepFocus) modalBox.innerHTML = '<div class="dv"><i class="dv-corner c1"></i><i class="dv-corner c2"></i><i class="dv-corner c3"></i><i class="dv-corner c4"></i><div class="dv-head"><div class="dv-who"><p class="dv-eyebrow">YORU IZAKAYA / RESERVATION DETAILS</p><h2 id="srModalTitle">Loading…</h2></div></div></div>';
        try {
            const res = await fetch(`${pageUrl}?handler=Details&id=${encodeURIComponent(id)}`, {
                credentials: 'same-origin', headers: { 'X-Requested-With': 'XMLHttpRequest' }, cache: 'no-store'
            });
            if (!res.ok) throw new Error(String(res.status));
            modalBox.innerHTML = await res.text();
            modalBox.focus();
        } catch {
            closeDetails();
            say('That reservation is no longer available. The list has been refreshed.', true);
            refreshBoard(true);
        }
    }

    function closeDetails() {
        if (modal.hidden) return;
        modal.hidden = true;
        modalBox.innerHTML = '';
        document.body.style.overflow = '';
        state.lastFocus?.focus?.();
    }

    // ---------------------------------------------------------------- small helpers
    async function copyText(text, el) {
        try {
            await navigator.clipboard.writeText(text);
        } catch {
            const t = document.createElement('textarea');
            t.value = text; t.style.position = 'fixed'; t.style.opacity = '0';
            document.body.appendChild(t); t.select();
            try { document.execCommand('copy'); } catch { /* ignored */ }
            t.remove();
        }
        say(`Copied ${text}`);
        closeMenu();
    }

    // ---------------------------------------------------------------- events
    root.addEventListener('click', e => {
        const t = e.target;
        const areaChip = t.closest('[data-area-filter]');
        if (areaChip) { setArea(areaChip.dataset.areaFilter); return; }
        const tab = t.closest('.sr-tab');
        if (tab) { setFilter(tab.dataset.filter); scrollToList(); return; }
        if (t.closest('[data-filter-jump]')) { setFilter(t.closest('[data-filter-jump]').dataset.filterJump); scrollToList(); return; }
        if (t.closest('[data-clear-filters]')) { clearFilters(); return; }

        const view = t.closest('.sr-seg button');
        if (view) { setView(view.dataset.view); return; }

        const copy = t.closest('[data-copy]');
        if (copy) { copyText(copy.dataset.copy, copy); return; }

        const act = t.closest('[data-act]');
        if (act) {
            const a = act.dataset.act;
            if (a === 'confirm') { confirmBooking(act.dataset.id || act.closest('[data-id]')?.dataset.id, act); return; }
            if (a === 'menu') { e.stopPropagation(); openMenu(act); return; }
            if (a === 'open') { openDetails(act.dataset.id || act.closest('[data-id]')?.dataset.id); return; }
            if (a === 'close') { closeDetails(); return; }
            if (a === 'call') { if (act.dataset.phone) location.href = 'tel:' + act.dataset.phone.replace(/[^+\d]/g, ''); closeMenu(); return; }
        }

        // a click anywhere else on a row opens the details
        const row = t.closest('.sr-row');
        if (row && !t.closest('a,button,input')) { openDetails(row.dataset.id); return; }

        if (!t.closest('.sr-menu')) closeMenu();
    });

    modal.addEventListener('click', e => { if (e.target === modal) closeDetails(); });

    document.addEventListener('keydown', e => {
        if (e.key !== 'Escape') return;
        if (!pop.hidden) { closePop(); dateBtn.focus(); return; }
        if (!menu.hidden) { const b = root.querySelector('.sr-more[aria-expanded="true"]'); closeMenu(); b?.focus(); }
        else if (!modal.hidden) closeDetails();
    });

    // keep focus inside the pop-up
    modal.addEventListener('keydown', e => {
        if (e.key !== 'Tab') return;
        const f = [...modalBox.querySelectorAll('button:not(:disabled),a[href],[tabindex]:not([tabindex="-1"])')].filter(x => !x.hidden && x.offsetParent !== null);
        if (!f.length) return;
        const first = f[0], last = f[f.length - 1];
        if (e.shiftKey && document.activeElement === first) { e.preventDefault(); last.focus(); }
        else if (!e.shiftKey && document.activeElement === last) { e.preventDefault(); first.focus(); }
    });

    window.addEventListener('resize', closeMenu);
    window.addEventListener('scroll', () => { if (!menu.hidden) closeMenu(); }, { passive: true });

    search?.addEventListener('input', () => { state.q = norm(search.value); applyFilters(); });
    search?.addEventListener('keydown', e => { if (e.key === 'Escape' && search.value) { search.value = ''; state.q = ''; applyFilters(); e.stopPropagation(); } });
    refreshBtn?.addEventListener('click', () => refreshBoard(false));

    // ---------------------------------------------------------------- date picker (the page's own calendar)
    const dateBtn = document.getElementById('srDateBtn');
    const pop = document.getElementById('srPop');
    const MONTHS = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'];
    const pad = n => String(n).padStart(2, '0');
    const iso = d => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
    const fromIso = s => { const [y, m, d] = s.split('-').map(Number); return new Date(y, m - 1, d); };
    let shown = null;
    let mode = 'days';   // 'days' | 'months' | 'years'

    function pendingDays() {
        const map = {};
        (root.querySelector('[data-part="summary"]')?.dataset.pendingDays || '').split(',').filter(Boolean)
            .forEach(p => { const [d, n] = p.split(':'); map[d] = Number(n) || 0; });
        return map;
    }

    function goToDate(value) {
        const u = new URL(pageUrl, location.origin);
        u.searchParams.set('date', value);
        if (state.view === 'timeline') u.searchParams.set('view', 'timeline');
        location.href = u.pathname + u.search;
    }

    // Month and year views: click the month name or the year in the header.
    function renderPickList(dir) {
        const y = shown.getFullYear(), m = shown.getMonth();
        const todayIso = root.dataset.todayDate || iso(new Date());
        const [ty, tm] = todayIso.split('-').map(Number);
        let title, items = '';
        if (mode === 'months') {
            title = `<b><button type="button" class="pp-title" data-mode="years">${y}</button></b>`;
            MONTHS.forEach((name, i) => {
                const cls = ['pm'];
                if (i === m) cls.push('is-sel');
                if (y === ty && i + 1 === tm) cls.push('is-today');
                items += `<button type="button" class="${cls.join(' ')}" data-m="${i}">${name.slice(0, 3)}</button>`;
            });
        } else {
            const from = y - (((y % 12) + 12) % 12);       // 12-year blocks
            title = `<b>${from} <span>– ${from + 11}</span></b>`;
            for (let k = 0; k < 12; k++) {
                const yy = from + k, cls = ['pm'];
                if (yy === y) cls.push('is-sel');
                if (yy === ty) cls.push('is-today');
                items += `<button type="button" class="${cls.join(' ')}" data-y="${yy}">${yy}</button>`;
            }
        }
        pop.innerHTML =
            `<div class="pp-head"><button type="button" class="pp-nav" data-pp="-1" aria-label="Previous">${icon('<path d="m14.5 5-7 7 7 7"/>')}</button>${title}` +
            `<button type="button" class="pp-nav" data-pp="1" aria-label="Next">${icon('<path d="m9.5 5 7 7-7 7"/>')}</button></div>` +
            `<div class="pp-mgrid${dir ? ' go-' + dir : ''}">${items}</div>` +
            `<div class="pp-foot"><span></span><button type="button" class="pp-today" data-pick="${todayIso}">Today</button></div>`;
    }

        function renderPop(dir) {
        if (mode !== 'days') { renderPickList(dir); return; }
        const y = shown.getFullYear(), m = shown.getMonth();
        const first = new Date(y, m, 1);
        const start = new Date(y, m, 1 - ((first.getDay() + 6) % 7));   // weeks start on Monday
        const todayIso = root.dataset.todayDate || iso(new Date());
        const pending = pendingDays();
        let cells = '';
        for (let i = 0; i < 42; i++) {
            const d = new Date(start.getFullYear(), start.getMonth(), start.getDate() + i);
            const key = iso(d);
            const cls = ['pd'];
            if (d.getMonth() !== m) cls.push('is-out');
            if (key === todayIso) cls.push('is-today');
            if (key === date) cls.push('is-sel');
            const n = pending[key];
            cells += `<button type="button" class="${cls.join(' ')}" data-pick="${key}"${key === date ? ' aria-current="date"' : ''}` +
                `${n ? ` title="${n} pending"` : ''}>${d.getDate()}${n ? '<i></i>' : ''}</button>`;
        }
        pop.innerHTML =
            `<div class="pp-head"><button type="button" class="pp-nav" data-pp="-1" aria-label="Previous month">${icon('<path d="m14.5 5-7 7 7 7"/>')}</button>` +
            `<b><button type="button" class="pp-title" data-mode="months">${MONTHS[m]}</button> <button type="button" class="pp-title is-year" data-mode="years">${y}</button></b>` +
            `<button type="button" class="pp-nav" data-pp="1" aria-label="Next month">${icon('<path d="m9.5 5 7 7-7 7"/>')}</button></div>` +
            `<div class="pp-week" aria-hidden="true">${['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'].map(x => `<span>${x}</span>`).join('')}</div>` +
            `<div class="pp-grid${dir ? ' go-' + dir : ''}">${cells}</div>` +
            `<div class="pp-foot"><span><i></i>Pending bookings</span><button type="button" class="pp-today" data-pick="${todayIso}">Today</button></div>`;
    }

    function openPop() {
        if (!pop.hidden) { closePop(); return; }
        shown = fromIso(date); shown.setDate(1);
        mode = 'days';
        renderPop();
        pop.hidden = false;
        dateBtn.setAttribute('aria-expanded', 'true');
        (pop.querySelector('.pd.is-sel') || pop.querySelector('.pd'))?.focus();
    }
    function closePop() {
        if (pop.hidden) return;
        pop.hidden = true;
        dateBtn.setAttribute('aria-expanded', 'false');
    }

    dateBtn?.addEventListener('click', e => { e.stopPropagation(); closeMenu(); openPop(); });
    pop?.addEventListener('click', e => {
        e.stopPropagation();
        const nav = e.target.closest('[data-pp]');
        if (nav) {
            const step = Number(nav.dataset.pp);
            shown = mode === 'days' ? new Date(shown.getFullYear(), shown.getMonth() + step, 1)
                  : mode === 'months' ? new Date(shown.getFullYear() + step, shown.getMonth(), 1)
                  : new Date(shown.getFullYear() + step * 12, shown.getMonth(), 1);
            renderPop(step > 0 ? 'next' : 'prev');
            pop.querySelector(`[data-pp="${step}"]`)?.focus();
            return;
        }
        const head = e.target.closest('[data-mode]');
        if (head) { mode = head.dataset.mode; renderPop(); pop.querySelector('.pm.is-sel')?.focus(); return; }
        const pm = e.target.closest('[data-m]');
        if (pm) { shown = new Date(shown.getFullYear(), Number(pm.dataset.m), 1); mode = 'days'; renderPop(); (pop.querySelector('.pd.is-sel') || pop.querySelector('.pd:not(.is-out)'))?.focus(); return; }
        const py = e.target.closest('[data-y]');
        if (py) { shown = new Date(Number(py.dataset.y), shown.getMonth(), 1); mode = 'months'; renderPop(); pop.querySelector('.pm.is-sel')?.focus(); return; }
        const pick = e.target.closest('[data-pick]');
        if (pick) goToDate(pick.dataset.pick);
    });
    
    document.addEventListener('click', e => { if (!pop.hidden && !e.target.closest('#srPop')) closePop(); });

    // ---------------------------------------------------------------- start
    const q = new URLSearchParams(location.search);
    if (FILTERS.includes(q.get('status'))) state.filter = q.get('status');
    if (q.get('q')) { state.q = norm(q.get('q')); if (search) search.value = q.get('q'); }
    setView(q.get('view') === 'timeline' ? 'timeline' : state.view);
    applyFilters();

    // first paint: stats, tabs and rows glide in once (not on every refresh)
    root.querySelectorAll('.sr-row').forEach((r, i) => r.style.setProperty('--i', Math.min(i, 14)));
    root.classList.add('is-boot');
    setTimeout(() => root.classList.remove('is-boot'), 1700);

    // Keep the day fresh while staff work: every 30 seconds, and when the tab comes back into view.
    setInterval(() => { if (!document.hidden && modal.hidden && menu.hidden) refreshBoard(true); }, 30000);
    document.addEventListener('visibilitychange', () => { if (!document.hidden) refreshBoard(true); });
})();
