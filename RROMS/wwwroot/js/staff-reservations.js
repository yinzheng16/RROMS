/* Owner: Member 2 — US16 staff "Today's reservations".
   Filtering and search run in the browser on the day that is already loaded.
   Server calls: ?handler=Board (refresh), ?handler=Details (pop-up), ?handler=Options (Modify dialog, GET),
   POST handlers: Confirm, Seat, NoShow, Cancel, Complete, Modify (each returns { success, code, message }). */
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
    const dlg = document.getElementById('srDialog');
    const dlgBox = document.getElementById('srDialogBox');
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
        x: '<path d="M6 6l12 12M18 6 6 18"/>',
        edit: '<path d="M4 20h4L19 9l-4-4L4 16v4Zm9-13 4 4"/>'
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
    // The same toast as the rest of the site: the markup is the one in _Layout.cshtml and the
    // styles are in site.css. kind is 'success' (default), 'info', or 'error' (when bad is true).
    const TOAST_KIND = {
        success: ['完了', 'All set', 'bi-check-lg'],
        error: ['注意', 'Oops', 'bi-exclamation-lg'],
        info: ['お知らせ', 'Notice', 'bi-info-lg']
    };
    function say(msg, bad, kind) {
        kind = bad ? 'error' : (kind || 'success');
        const [jp, title, icon] = TOAST_KIND[kind];

        document.querySelectorAll('.yoru-toast-wrap').forEach(w => w.remove());

        const wrap = document.createElement('div');
        wrap.className = 'yoru-toast-wrap';
        wrap.innerHTML =
            `<div class="yoru-toast yoru-toast--${kind}" role="${bad ? 'alert' : 'status'}" aria-live="polite">` +
            `<span class="yoru-toast-seal" aria-hidden="true"><i class="bi ${icon}"></i></span>` +
            `<div class="yoru-toast-body"><p class="yoru-toast-title"><span class="yoru-toast-jp">${jp}</span>${title}</p>` +
            `<p class="yoru-toast-msg"></p></div>` +
            `<button type="button" class="yoru-toast-close" aria-label="Close notification"><i class="bi bi-x-lg"></i></button>` +
            `<span class="yoru-toast-bar" aria-hidden="true"></span></div>`;
        wrap.querySelector('.yoru-toast-msg').textContent = msg;
        document.body.appendChild(wrap);

        // Closes when the gold line runs out (5 s, paused on hover) or when the x is clicked.
        const box = wrap.firstElementChild;
        const hide = () => {
            if (box.classList.contains('is-leaving')) return;
            box.classList.add('is-leaving');
            setTimeout(() => wrap.remove(), 400);
        };
        box.querySelector('.yoru-toast-bar').addEventListener('animationend', hide);
        box.querySelector('.yoru-toast-close').addEventListener('click', hide);
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
                say('Up to date · ' + new Date().toLocaleTimeString('en-US', { hour: 'numeric', minute: '2-digit' }), false, 'info');
            }
        } catch {
            if (!quiet) say('Could not refresh. Check your connection and try again.', true);
        } finally {
            state.busy = false;
            refreshBtn?.classList.remove('is-spin');
        }
    }

    // One POST to a page handler. Returns { ok, data }. Never throws.
    async function post(handler, id, fields) {
        try {
            const res = await fetch(`${pageUrl}?handler=${handler}&id=${encodeURIComponent(id)}`, {
                method: 'POST', credentials: 'same-origin',
                headers: {
                    'X-Requested-With': 'XMLHttpRequest', 'RequestVerificationToken': token(),
                    'Content-Type': 'application/x-www-form-urlencoded'
                },
                body: new URLSearchParams(fields || {}).toString()
            });
            let data = {};
            try { data = await res.json(); } catch { /* not JSON */ }
            return { ok: res.ok && data.success, data };
        } catch {
            return { ok: false, data: { message: 'We could not reach the server. Refresh and check this booking before trying again.' } };
        }
    }

    const FAIL = {
        confirm: 'Could not confirm this reservation. Refresh and try again.',
        seat: 'Could not seat these guests. Refresh and try again.',
        noshow: 'Could not mark this booking as a no-show. Refresh and try again.',
        cancel: 'Could not cancel this booking. Refresh and try again.',
        complete: 'Could not complete this visit. Refresh and try again.',
        modify: 'Could not update this booking. Refresh and try again.'
    };
    const HANDLER = { confirm: 'Confirm', seat: 'Seat', noshow: 'NoShow', cancel: 'Cancel', complete: 'Complete', modify: 'Modify' };

    // Runs one action, shows the answer, refreshes the page parts and the open pop-up.
    async function runAction(name, id, fields) {
        if (!id) return { ok: false, data: {} };
        const buttons = root.querySelectorAll(`[data-act="${name}"][data-id="${id}"]`);
        buttons.forEach(b => { b.disabled = true; });
        closeMenu();
        const r = await post(HANDLER[name], id, fields);
        say(r.data.message || (r.ok ? 'Done.' : FAIL[name]), !r.ok);
        await refreshBoard(true);
        if (!modal.hidden && modalBox.querySelector('.dv')?.dataset.id === String(id)) await openDetails(id, true);
        buttons.forEach(b => { b.disabled = false; });
        return r;
    }

    // ---------------------------------------------------------------- small dialog (confirm / reason / modify)
    function openDialog(html) {
        state.dlgFocus = document.activeElement;
        dlgBox.innerHTML = html;
        dlg.hidden = false;
        dlgBox.querySelector('[autofocus],input,select,textarea,button.dg-ok')?.focus();
    }
    function closeDialog() {
        if (dlg.hidden) return;
        dlg.hidden = true;
        dlgBox.innerHTML = '';
        state.dlgFocus?.focus?.();
    }

    function bookingLine(d) {
        return `${esc(d.name)} · ${esc(d.time)} · ${d.guests} guest${d.guests === '1' ? '' : 's'}${d.table ? ' · table ' + esc(d.table) : ''}`;
    }

    // ask: { title, lead, d, okLabel, danger, reason }
    function ask(o) {
        return new Promise(resolve => {
            openDialog(
                `<form class="dg" novalidate><header><h3>${esc(o.title)}</h3><p>${bookingLine(o.d)}</p></header>` +
                `<p class="dg-lead">${o.lead}</p>` +
                (o.reason ? `<label class="dg-field">${esc(o.reason)}<textarea name="reason" rows="2" maxlength="200" placeholder="Optional. The customer will see this."></textarea></label>` : '') +
                `<footer><button type="submit" class="dg-ok${o.danger ? ' is-danger' : ''}">${esc(o.okLabel)}</button>` +
                `<button type="button" class="dg-no" data-dg="cancel">Keep booking</button></footer></form>`);
            const form = dlgBox.querySelector('form');
            form.addEventListener('submit', ev => {
                ev.preventDefault();
                const v = form.querySelector('[name="reason"]')?.value.trim() || '';
                closeDialog();
                resolve({ yes: true, reason: v });
            });
            dlgBox.querySelector('[data-dg="cancel"]').addEventListener('click', () => { closeDialog(); resolve({ yes: false }); });
            dlg._cancel = () => { closeDialog(); resolve({ yes: false }); };
        });
    }

    function dataOf(id) {
        const host = root.querySelector(`.sr-row[data-id="${id}"], .tl-bar[data-id="${id}"]`);
        if (host) return host.dataset;
        // opened from the pop-up of a booking that is not in the list (should not happen): minimal data
        const dk = modalBox.querySelector('.dv');
        return { id: String(id), name: dk?.querySelector('h2')?.textContent || 'Guest', time: '', guests: '', table: '' };
    }

    async function seat(id) { await runAction('seat', id); }

    async function noShow(id) {
        const d = dataOf(id);
        const r = await ask({
            title: 'Mark as no-show?', d, okLabel: 'Mark as no-show', danger: true,
            lead: 'The guests did not arrive. The table is released and the customer is told. This cannot be undone here.'
        });
        if (r.yes) await runAction('noshow', id);
    }

    async function cancelBooking(id) {
        const d = dataOf(id);
        const r = await ask({
            title: 'Cancel this booking?', d, okLabel: 'Cancel booking', danger: true, reason: 'Reason (optional)',
            lead: 'The table is released and the customer is notified by app message.'
        });
        if (r.yes) await runAction('cancel', id, { reason: r.reason });
    }

    async function complete(id) {
        const d = dataOf(id);
        const r = await ask({
            title: 'Guests have left?', d, okLabel: 'Yes, guests left',
            lead: 'This ends the dining session, completes the reservation and frees the table. Guests can no longer order from this table. Paying the bill does not do this by itself.'
        });
        if (r.yes) await runAction('complete', id);
    }

    // ---- Modify ----
    // The dialog has its own pickers (date calendar, time grid, guest stepper, table list) so it matches the
    // rest of the page instead of the browser's plain controls.
    const DAYS = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];
    const CHEV = icon('<path d="m6 9 6 6 6-6"/>');
    const CAL_ICON = icon('<rect x="3.5" y="5" width="17" height="15" rx="2"/><path d="M3.5 10h17M8 3v4m8-4v4"/>');
    const dayText = s => { const x = fromIso(s); return `${DAYS[x.getDay()]}, ${x.getDate()} ${MONTHS[x.getMonth()].slice(0, 3)} ${x.getFullYear()}`; };

    async function modify(id) {
        const d = dataOf(id);
        closeMenu();
        const todayIso = root.dataset.todayDate || iso(new Date());
        const st = { date: d.date || date, time: d.time24 || '', guests: Number(d.guests) || 2, table: Number(d.tableId) || 0 };
        let data = null, openKey = null, calMode = 'days', calShown = fromIso(st.date);
        calShown.setDate(1);

        const field = (key, label, inner) => `<div class="dg-field f-${key}"><span class="dg-label">${label}</span>${inner}</div>`;
        const dd = (key, ico, cls) =>
            `<div class="dd" data-dd="${key}"><button type="button" class="dd-btn" aria-haspopup="listbox" aria-expanded="false"><span class="dd-val">Loading…</span>${ico}</button>` +
            `<div class="dd-pop ${cls}" role="listbox" hidden></div></div>`;

        openDialog(`<form class="dg dg-wide" novalidate><header><h3>Modify reservation</h3><p>${bookingLine(d)}</p></header>` +
            `<div class="dg-grid">` +
            field('date', 'Date', dd('date', CAL_ICON, 'dd-cal')) +
            field('time', 'Time', dd('time', CHEV, 'dd-times')) +
            field('guests', 'Guests', `<div class="dd-step"><button type="button" data-step="-1" aria-label="One guest fewer">&minus;</button>` +
                `<input type="number" name="guests" min="1" max="50" value="${st.guests}" inputmode="numeric" aria-label="Number of guests">` +
                `<button type="button" data-step="1" aria-label="One guest more">+</button></div>`) +
            field('table', 'Table', dd('table', CHEV, 'dd-tables')) +
            field('request', `Special request <em>${d.request ? 'entered by the guest, you can edit it' : 'the guest did not add one, add it here if they ask'}</em>`,
                `<textarea name="specialRequest" rows="3" maxlength="500" placeholder="Allergy, birthday, high chair, or anything the kitchen should know">${esc(d.request || '')}</textarea>` +
                `<span class="dg-count" aria-live="off"></span>`) +
            `</div><p class="dg-msg" role="alert" hidden></p>` +
            `<footer><button type="submit" class="dg-ok" disabled>Save changes</button>` +
            `<button type="button" class="dg-no" data-dg="cancel">Close</button></footer></form>`);

        const form = dlgBox.querySelector('form');
        const msg = form.querySelector('.dg-msg');
        const okBtn = form.querySelector('.dg-ok');
        const gIn = form.elements.guests;
        const reqIn = form.elements.specialRequest;
        const counter = form.querySelector('.dg-count');
        const box = k => form.querySelector(`.dd[data-dd="${k}"]`);
        const btn = k => box(k).querySelector('.dd-btn');
        const popOf = k => box(k).querySelector('.dd-pop');
        const setVal = (k, html) => { btn(k).querySelector('.dd-val').innerHTML = html; };
        const showMsg = t => { msg.textContent = t || ''; msg.hidden = !t; };
        let seq = 0, pending = Promise.resolve(), gTimer, base = null, guardEl = null;

        // ---- unsaved changes: ask before throwing them away ----
        const snapshot = () => JSON.stringify([st.date, st.time, st.guests, st.table, reqIn.value.trim()]);
        const dirty = () => base !== null && snapshot() !== base;
        const lockForm = on => [...form.children].forEach(c => { if (c !== guardEl) c.inert = on; });
        function hideGuard() {
            if (!guardEl) return;
            guardEl.remove(); guardEl = null; lockForm(false);
            reqIn.focus({ preventScroll: true });
        }
        function tryClose() {
            if (guardEl) return;
            if (!dirty()) { closeDialog(); return; }
            closeDD();
            guardEl = document.createElement('div');
            guardEl.className = 'dg-guard';
            guardEl.setAttribute('role', 'alertdialog');
            guardEl.setAttribute('aria-label', 'Unsaved changes');
            guardEl.innerHTML = `<div class="dg-guard-box"><h4>Discard your changes?</h4>` +
                `<p>This reservation has changes that are not saved yet.</p>` +
                `<div class="dg-guard-btns"><button type="button" class="dg-ok" data-guard="keep">Keep editing</button>` +
                `<button type="button" class="dg-no is-discard" data-guard="discard">Discard</button></div></div>`;
            form.appendChild(guardEl);
            lockForm(true);
            guardEl.querySelector('[data-guard="keep"]').focus();
            guardEl.addEventListener('click', e => {
                const g = e.target.closest('[data-guard]');
                if (!g) return;
                if (g.dataset.guard === 'keep') hideGuard(); else { guardEl = null; closeDialog(); }
            });
        }

        // ---- open / close one drop-down ----
        function closeDD() {
            if (!openKey) return;
            const b = box(openKey), p = b.querySelector('.dd-pop');
            b.classList.remove('is-open');
            b.querySelector('.dd-btn').setAttribute('aria-expanded', 'false');
            p.hidden = true; p.classList.remove('is-up');
            openKey = null;
        }
        function openDD(k) {
            if (btn(k).disabled) return;
            if (openKey === k) { closeDD(); return; }
            closeDD();
            if (k === 'date') { calMode = 'days'; calShown = fromIso(st.date); calShown.setDate(1); renderCal(); }
            const p = popOf(k);
            p.hidden = false;
            box(k).classList.add('is-open');
            btn(k).setAttribute('aria-expanded', 'true');
            openKey = k;
            // open upwards when the screen is tighter below than above
            const r = btn(k).getBoundingClientRect();
            const below = window.innerHeight - r.bottom - 16, above = r.top - 16;
            const need = k === 'date' ? 360 : 250;
            const up = below < need && above > below;
            p.classList.toggle('is-up', up);
            p.style.maxHeight = Math.max(170, Math.min(k === 'date' ? 440 : 320, up ? above : below)) + 'px';
            p.querySelector('.is-sel')?.scrollIntoView({ block: 'nearest' });
        }

        // ---- time grid ----
        function renderTimes() {
            popOf('time').innerHTML = data.slots.map(s =>
                `<button type="button" class="dd-opt${s.value === st.time ? ' is-sel' : ''}${s.value === d.time24 && st.date === d.date ? ' is-current' : ''}" role="option" aria-selected="${s.value === st.time}" data-v="${esc(s.value)}">${esc(s.label)}</button>`).join('');
            const cur = data.slots.find(s => s.value === st.time);
            setVal('time', cur ? esc(cur.label) : '&mdash;');
        }

        // ---- table list, grouped by dining area ----
        function renderTables() {
            let last = null, html = '';
            data.tables.forEach(t => {
                if (t.area !== last) { last = t.area; html += `<div class="dd-group">${esc(t.area)}</div>`; }
                html += `<button type="button" class="dd-row${t.id === st.table ? ' is-sel' : ''}${t.id === Number(d.tableId) ? ' is-current' : ''}" role="option" aria-selected="${t.id === st.table}" data-id="${t.id}"${t.free ? '' : ' disabled'}>` +
                    `<b>${esc(t.label)}</b><span>seats ${t.capacity}</span>${t.free ? '' : `<em>${esc(t.note || 'unavailable')}</em>`}</button>`;
            });
            popOf('table').innerHTML = html;
            const cur = data.tables.find(t => t.id === st.table);
            setVal('table', cur ? `<b>${esc(cur.label)}</b><small>${esc(cur.area)} &middot; seats ${cur.capacity}</small>` : 'No table is free');
        }

        // ---- calendar (days, months, years) ----
        function renderCal() {
            const p = popOf('date');
            const y = calShown.getFullYear(), m = calShown.getMonth();
            const [ty, tm] = todayIso.split('-').map(Number);
            const head = (title, canPrev) =>
                `<div class="pp-head"><button type="button" class="pp-nav" data-pp="-1" aria-label="Previous"${canPrev ? '' : ' disabled'}>${icon('<path d="m14.5 5-7 7 7 7"/>')}</button>${title}` +
                `<button type="button" class="pp-nav" data-pp="1" aria-label="Next">${icon('<path d="m9.5 5 7 7-7 7"/>')}</button></div>`;

            if (calMode === 'months') {
                p.innerHTML = head(`<b><button type="button" class="pp-title" data-mode="years">${y}</button></b>`, y > ty) +
                    `<div class="pp-mgrid">` + MONTHS.map((n, i) =>
                        `<button type="button" class="pm${i === m ? ' is-sel' : ''}${y === ty && i + 1 === tm ? ' is-today' : ''}" data-m="${i}"${y === ty && i + 1 < tm ? ' disabled' : ''}>${n.slice(0, 3)}</button>`).join('') + `</div>`;
                return;
            }
            if (calMode === 'years') {
                const from = y - (((y % 12) + 12) % 12);
                let cells = '';
                for (let k = 0; k < 12; k++) {
                    const yy = from + k;
                    cells += `<button type="button" class="pm${yy === y ? ' is-sel' : ''}${yy === ty ? ' is-today' : ''}" data-y="${yy}"${yy < ty ? ' disabled' : ''}>${yy}</button>`;
                }
                p.innerHTML = head(`<b>${from} <span>&ndash; ${from + 11}</span></b>`, from > ty) + `<div class="pp-mgrid">${cells}</div>`;
                return;
            }
            const first = new Date(y, m, 1);
            const start = new Date(y, m, 1 - ((first.getDay() + 6) % 7));   // weeks start on Monday
            let cells = '';
            for (let i = 0; i < 42; i++) {
                const x = new Date(start.getFullYear(), start.getMonth(), start.getDate() + i);
                const key = iso(x), cls = ['pd'];
                if (x.getMonth() !== m) cls.push('is-out');
                if (key === todayIso) cls.push('is-today');
                if (key === st.date) cls.push('is-sel');
                cells += `<button type="button" class="${cls.join(' ')}" data-pick="${key}"${key < todayIso ? ' disabled' : ''}>${x.getDate()}</button>`;
            }
            p.innerHTML = head(`<b><button type="button" class="pp-title" data-mode="months">${MONTHS[m]}</button> <button type="button" class="pp-title is-year" data-mode="years">${y}</button></b>`, y * 12 + m > ty * 12 + tm - 1) +
                `<div class="pp-week" aria-hidden="true">${['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'].map(x => `<span>${x}</span>`).join('')}</div>` +
                `<div class="pp-grid">${cells}</div>` +
                `<div class="pp-foot"><span></span><button type="button" class="pp-today" data-pick="${todayIso}">Today</button></div>`;
        }

        function calClick(t) {
            const y = calShown.getFullYear(), m = calShown.getMonth();
            const nav = t.closest('[data-pp]');
            if (nav) {
                const s = Number(nav.dataset.pp);
                calShown = calMode === 'days' ? new Date(y, m + s, 1) : calMode === 'months' ? new Date(y + s, m, 1) : new Date(y + s * 12, m, 1);
                renderCal(); return;
            }
            const hd = t.closest('[data-mode]');
            if (hd) { calMode = hd.dataset.mode; renderCal(); return; }
            const pm = t.closest('[data-m]');
            if (pm) { calShown = new Date(y, Number(pm.dataset.m), 1); calMode = 'days'; renderCal(); return; }
            const py = t.closest('[data-y]');
            if (py) { calShown = new Date(Number(py.dataset.y), m, 1); calMode = 'months'; renderCal(); return; }
            const pk = t.closest('[data-pick]');
            if (pk) { st.date = pk.dataset.pick; setVal('date', dayText(st.date)); closeDD(); pending = load(); }
        }

        // ---- ask the server which times and tables fit ----
        async function load() {
            const my = ++seq;
            showMsg('');
            closeDD();
            okBtn.disabled = true;
            btn('time').disabled = btn('table').disabled = true;
            const qs = new URLSearchParams({ handler: 'Options', id, date: st.date, time: st.time || '', guests: String(st.guests) });
            let res;
            try {
                const r = await fetch(`${pageUrl}?${qs}`, { credentials: 'same-origin', headers: { 'X-Requested-With': 'XMLHttpRequest' }, cache: 'no-store' });
                if (!r.ok) throw new Error();
                res = await r.json();
            } catch { if (my === seq) showMsg('Could not load times and tables. Close and try again.'); return; }
            if (my !== seq) return;

            if (res.closed) {
                setVal('time', '&mdash;'); setVal('table', '&mdash;');
                showMsg(res.closedReason || 'No times on this day.');
                return;
            }
            data = res;
            st.time = data.time;
            renderTimes();
            const free = data.tables.filter(x => x.free);
            const pick = data.tables.find(x => x.id === st.table && x.free) || free.find(x => x.label === d.table) || free[0];
            st.table = pick ? pick.id : 0;
            renderTables();
            btn('time').disabled = false;
            btn('table').disabled = !free.length;
            if (!free.length) showMsg('No table is free for this date, time and party size.');
            okBtn.disabled = !free.length;
            if (base === null) base = snapshot();      // what the dialog looked like when it opened
        }

        function setGuests(n, now) {
            n = Math.max(1, Math.min(50, Math.round(n) || 1));
            st.guests = n; gIn.value = n;
            clearTimeout(gTimer);
            gTimer = setTimeout(() => { pending = load(); }, now ? 0 : 350);
        }

        form.addEventListener('click', e => {
            const t = e.target;
            const head = t.closest('.dd-btn');
            if (head) { openDD(head.closest('.dd').dataset.dd); return; }
            const step = t.closest('[data-step]');
            if (step) { setGuests(st.guests + Number(step.dataset.step)); return; }
            if (t.closest('.dd-cal')) { calClick(t); return; }
            const opt = t.closest('.dd-opt');
            if (opt) { st.time = opt.dataset.v; closeDD(); renderTimes(); pending = load(); return; }
            const row = t.closest('.dd-row');
            if (row && !row.disabled) { st.table = Number(row.dataset.id); closeDD(); renderTables(); return; }
            if (!t.closest('.dd')) closeDD();
        });
        gIn.addEventListener('change', () => setGuests(Number(gIn.value), true));
        const count = () => { counter.textContent = `${reqIn.value.length} / 500`; };
        reqIn.addEventListener('input', count);
        count();
        form.addEventListener('keydown', e => {
            if (e.key !== 'Escape') return;
            if (guardEl) { e.stopPropagation(); hideGuard(); return; }
            if (!openKey) return;
            e.stopPropagation();
            const k = openKey; closeDD(); btn(k).focus();
        });

        form.addEventListener('submit', async ev => {
            ev.preventDefault();
            closeDD();
            await pending;            // a reload may still be running
            if (okBtn.disabled) return;
            okBtn.disabled = true;
            const r = await post('Modify', id, { date: st.date, time: st.time, guests: String(st.guests), tableId: String(st.table), specialRequest: reqIn.value.trim() });
            if (r.ok) {
                closeDialog();
                say(r.data.message || 'Reservation updated.');
                await refreshBoard(true);
                if (!modal.hidden && modalBox.querySelector('.dv')?.dataset.id === String(id)) await openDetails(id, true);
            } else {
                showMsg(r.data.message || FAIL.modify);
                okBtn.disabled = false;
                if (r.data.code === 'status_changed') refreshBoard(true);
            }
        });
        form.querySelector('[data-dg="cancel"]').addEventListener('click', tryClose);
        dlg._cancel = tryClose;

        setVal('date', dayText(st.date));
        pending = load();
    }

    // ---------------------------------------------------------------- "..." menu
    function closeMenu() {
        if (menu.hidden) return;
        menu.hidden = true;
        root.querySelectorAll('.sr-more[aria-expanded="true"]').forEach(b => b.setAttribute('aria-expanded', 'false'));
    }

    // Only the actions that apply to this booking right now (the server checks again).
    function statusItems(d) {
        const it = (act, ico, label, cls) =>
            `<button type="button" class="m-item${cls ? ' ' + cls : ''}" role="menuitem" data-act="${act}" data-id="${esc(d.id)}">${icon(ICONS[ico])}<span>${label}</span></button>`;
        const list = [];
        if (d.seat === '1') list.push(it('seat', 'seat', 'Mark as seated'));
        if (d.complete === '1') list.push(it('complete', 'check', 'Guests left'));
        if (d.modify === '1') list.push(it('modify', 'edit', 'Modify booking'));
        if (d.noshow === '1') list.push(it('noshow', 'noshow', 'Mark as no-show', 'is-warn'));
        if (d.cancel === '1') list.push(it('cancel', 'x', 'Cancel booking', 'is-warn'));
        return list.length ? `<div class="m-sep">CHANGE STATUS</div>` + list.join('') : '';
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
            statusItems(d);

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
        say(`Copied ${text}`, false, 'info');
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
            const aid = act.dataset.id || act.closest('[data-id]')?.dataset.id;
            if (a === 'confirm') { runAction('confirm', aid); return; }
            if (a === 'seat') { seat(aid); return; }
            if (a === 'noshow') { closeMenu(); noShow(aid); return; }
            if (a === 'cancel') { closeMenu(); cancelBooking(aid); return; }
            if (a === 'complete') { closeMenu(); complete(aid); return; }
            if (a === 'modify') { closeMenu(); modify(aid); return; }
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

    // US19: this guest's earlier bookings (read-only). One booking is always selected and shown on the right.
    // Pages are 5 bookings each; a new page selects its first booking.
    modalBox.addEventListener('click', e => {
        // dining preferences: a long text is cut to three lines until View more is pressed
        const prefMore = e.target.closest('[data-pref-more]');
        if (prefMore) {
            const open = prefMore.closest('.dv-pref').classList.toggle('is-open');
            prefMore.setAttribute('aria-expanded', String(open));
            prefMore.firstElementChild.textContent = open ? 'Show less' : 'View more';
            return;
        }
        const band = e.target.closest('[data-past]');
        if (!band) return;
        const select = card => {
            band.querySelectorAll('[data-past-card]').forEach(c => {
                const on = c === card;
                c.setAttribute('aria-expanded', String(on));
                const box = document.getElementById(c.getAttribute('aria-controls'));
                if (box) box.hidden = !on;
            });
        };

        const card = e.target.closest('[data-past-card]');
        if (card) { select(card); return; }

        const go = e.target.closest('[data-past-go]');
        if (!go || go.disabled) return;
        const items = [...band.querySelectorAll('[data-past-list] > li')];
        const pages = Math.max(1, ...items.map(li => Number(li.dataset.page) || 1));
        const now = Number(band.querySelector('.dv-pager [aria-current="page"]')?.dataset.pastGo) || 1;
        const target = go.dataset.pastGo === 'prev' ? now - 1 : go.dataset.pastGo === 'next' ? now + 1 : Number(go.dataset.pastGo);
        if (!(target >= 1 && target <= pages)) return;

        items.forEach(li => { li.hidden = Number(li.dataset.page) !== target; });
        select(items.find(li => !li.hidden)?.querySelector('[data-past-card]'));
        band.querySelectorAll('.dv-pager [data-past-go]').forEach(b => {
            const k = b.dataset.pastGo;
            if (k === 'prev') b.disabled = target === 1;
            else if (k === 'next') b.disabled = target === pages;
            else {
                const on = Number(k) === target;
                b.classList.toggle('is-on', on);
                if (on) b.setAttribute('aria-current', 'page'); else b.removeAttribute('aria-current');
            }
        });
        const from = (target - 1) * 5 + 1;
        const to = Math.min(items.length, target * 5);
        const info = band.querySelector('[data-past-info]');
        if (info) info.textContent = `Showing ${from}\u2013${to} of ${items.length}`;
        band.querySelector(`.dv-pager [data-past-go="${target}"]`)?.focus({ preventScroll: true });
    });

    modal.addEventListener('click', e => { if (e.target === modal) closeDetails(); });
    dlg.addEventListener('click', e => { if (e.target === dlg) (dlg._cancel || closeDialog)(); });
    dlg.addEventListener('keydown', e => {
        if (e.key !== 'Tab') return;
        const f = [...dlgBox.querySelectorAll('button:not(:disabled),input:not(:disabled),select:not(:disabled),textarea')].filter(x => x.offsetParent !== null);
        if (!f.length) return;
        const first = f[0], last = f[f.length - 1];
        if (e.shiftKey && document.activeElement === first) { e.preventDefault(); last.focus(); }
        else if (!e.shiftKey && document.activeElement === last) { e.preventDefault(); first.focus(); }
    });

    document.addEventListener('keydown', e => {
        if (e.key !== 'Escape') return;
        if (!pop.hidden) { closePop(); dateBtn.focus(); return; }
        if (!dlg.hidden) { (dlg._cancel || closeDialog)(); return; }
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
    setInterval(() => { if (!document.hidden && modal.hidden && menu.hidden && dlg.hidden) refreshBoard(true); }, 30000);
    document.addEventListener('visibilitychange', () => { if (!document.hidden) refreshBoard(true); });
})();
