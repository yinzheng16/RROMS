// US12 customer reservation page (Member 2).
// Every piece of text from the server or the customer is written with textContent, never innerHTML.
(() => {
    'use strict';

    const $ = id => document.getElementById(id);
    const cfg = JSON.parse($('rsvConfig').textContent);
    const reduceMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    const delay = ms => new Promise(r => setTimeout(r, reduceMotion ? 0 : ms));

    const WEEK = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];
    const MONTHS = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'];
    const DRAFT_KEY = 'rsv.draft';

    const state = {
        step: 1,
        date: null,       // "yyyy-MM-dd"
        time: null,       // "HH:mm"
        timeLabel: null,  // "6:30 PM"
        guests: 2,
        typeId: null,
        type: null,       // the option object chosen in step 2
        special: '',
        accepted: false,  // minimum spend accepted
        token: null,      // one id per booking attempt, so a double click cannot book twice
        busy: false,
        view: null,       // {y, m} month shown by the calendar
        overlap: false,   // the customer already has a booking at the chosen time
        custom: false     // the time came from "Other time" (not one of the 30-minute buttons)
    };

    const dateSet = new Set(cfg.dates);
    let slotRequest = 0;
    let typeRequest = 0;

    // ---------- small helpers ----------
    const parseDate = iso => { const [y, m, d] = iso.split('-').map(Number); return new Date(y, m - 1, d); };
    const fmtDate = iso => { const d = parseDate(iso); return `${WEEK[d.getDay()]}, ${d.getDate()} ${MONTHS[d.getMonth()].slice(0, 3)} ${d.getFullYear()}`; };
    const fmtMoney = n => 'RM ' + Number(n).toLocaleString('en-MY', { minimumFractionDigits: n % 1 ? 2 : 0, maximumFractionDigits: 2 });
    const plural = (n, one, many) => `${n} ${n === 1 ? one : many}`;
    const iso = (y, m, d) => `${y}-${String(m + 1).padStart(2, '0')}-${String(d).padStart(2, '0')}`;

    function el(tag, className, text) {
        const node = document.createElement(tag);
        if (className) node.className = className;
        if (text !== undefined && text !== null) node.textContent = text;
        return node;
    }

    function showError(step, message) {
        const box = $('rsvError' + step);
        box.textContent = message || '';
        box.hidden = !message;
    }

    function newToken() {
        if (window.crypto && crypto.randomUUID) return crypto.randomUUID();
        return 'r' + Date.now().toString(36) + Math.random().toString(36).slice(2, 10);
    }

    // Any change to what is being booked starts a new attempt and asks for the spend again.
    function touch() {
        state.accepted = false;
        state.token = null;
        saveDraft();
    }

    function saveDraft() {
        try {
            sessionStorage.setItem(DRAFT_KEY, JSON.stringify({
                date: state.date, time: state.time, guests: state.guests,
                typeId: state.typeId, special: state.special
            }));
        } catch { /* storage may be blocked; the page still works */ }
    }

    function loadDraft() {
        try {
            const d = JSON.parse(sessionStorage.getItem(DRAFT_KEY) || 'null');
            if (!d) return;
            if (d.date && dateSet.has(d.date)) { state.date = d.date; state.time = d.time || null; }
            if (Number.isInteger(d.guests) && d.guests >= 1 && d.guests <= cfg.maxGuests) state.guests = d.guests;
            if (Number.isInteger(d.typeId)) state.typeId = d.typeId;
            if (typeof d.special === 'string') state.special = d.special.slice(0, 500);
        } catch { /* ignore a damaged draft */ }
    }

    // Prefill from the home page widget: /Customer/Reservations/Create?date=2026-10-27&time=19:00&guests=2
    // Anything invalid is ignored, so a wrong link never breaks the page.
    function applyQueryPrefill() {
        let q;
        try { q = new URLSearchParams(window.location.search); } catch { return; }
        const date = q.get('date'), time = q.get('time'), guests = q.get('guests');
        if (!date && !time && !guests) return;

        if (date && dateSet.has(date)) { state.date = date; state.time = null; state.timeLabel = null; }

        // accept "19:00" and also "7:00 PM"
        if (time && state.date) {
            const m = /^(\d{1,2}):(\d{2})\s*(AM|PM)?$/i.exec(time.trim());
            if (m) {
                let h = parseInt(m[1], 10);
                if (m[3]) { const pm = m[3].toUpperCase() === 'PM'; h = (h % 12) + (pm ? 12 : 0); }
                if (h >= 0 && h < 24) state.time = String(h).padStart(2, '0') + ':' + m[2];
            }
        }

        const g = parseInt(guests, 10);
        if (Number.isInteger(g) && g >= 1 && g <= cfg.maxGuests) state.guests = g;

        state.accepted = false; state.token = null;
        saveDraft();
        try { history.replaceState(null, '', window.location.pathname); } catch { /* ignore */ }
    }
    function clearDraft() { try { sessionStorage.removeItem(DRAFT_KEY); } catch { /* ignore */ } }

    // ---------- summary (right) ----------
    function setSum(id, text) {
        const node = $(id);
        if (node.textContent === text) return;
        node.textContent = text;
        const row = node.closest('li');
        if (row) { row.classList.remove('is-flash'); void row.offsetWidth; row.classList.add('is-flash'); }
    }

    // The dining area icon is one of the symbols in _AreaIcons.cshtml (the same icons the admin picks from).
    function areaIconHref(name) {
        return '#area-' + (/^[a-z-]+$/.test(name || '') ? name : 'map-pin');
    }

    function areaIcon(name) {
        const NS = 'http://www.w3.org/2000/svg';
        const svg = document.createElementNS(NS, 'svg');
        svg.setAttribute('class', 'area-ico');
        svg.setAttribute('aria-hidden', 'true');
        const use = document.createElementNS(NS, 'use');
        use.setAttribute('href', areaIconHref(name));
        svg.appendChild(use);
        return svg;
    }

    function updateSummary() {
        setSum('sumType', state.type ? state.type.name : 'Not chosen yet');
        const jp = $('sumTypeJp');
        jp.textContent = state.type && state.type.japaneseName ? state.type.japaneseName : '';
        const areaName = state.type && state.type.areaName ? state.type.areaName : '';
        $('sumTypeArea').textContent = areaName;
        $('sumTypeAreaLine').hidden = !areaName;
        $('sumTypeAreaIco').setAttribute('href', areaIconHref(state.type && state.type.areaIcon));
        setSum('sumDate', state.date ? fmtDate(state.date) : '-');
        setSum('sumTime', state.timeLabel || '-');
        setSum('sumGuests', plural(state.guests, 'guest', 'guests'));
        setSum('sumSpecial', state.special.trim() || 'None added');

        const spendRow = document.querySelector('[data-sum="spend"]');
        spendRow.hidden = !state.type;
        if (state.type) setSum('sumSpend', state.type.minimumSpend > 0 ? fmtMoney(state.type.minimumSpend) : 'No minimum spend');

        // Edit shortcuts appear once there is something to go back to.
        const show = { type: state.step >= 3, date: state.step >= 2, time: state.step >= 2, guests: state.step >= 2, special: state.step >= 3 };
        Object.entries(show).forEach(([key, on]) => {
            const btn = document.querySelector(`[data-sum="${key}"] .rsv-link`);
            if (btn) btn.hidden = !on || state.busy;
        });
    }

    // ---------- step 1: calendar, time, guests ----------
    function initView() {
        const base = state.date ? parseDate(state.date) : (cfg.dates.length ? parseDate(cfg.dates[0]) : new Date());
        state.view = { y: base.getFullYear(), m: base.getMonth() };
    }

    function renderCalendar() {
        const { y, m } = state.view;
        $('rsvCalTitle').textContent = `${MONTHS[m]} ${y}`;

        const grid = $('rsvCalGrid');
        grid.replaceChildren();

        const first = new Date(y, m, 1);
        const lead = (first.getDay() + 6) % 7;                 // Monday first
        const days = new Date(y, m + 1, 0).getDate();
        const todayIso = iso(new Date().getFullYear(), new Date().getMonth(), new Date().getDate());

        for (let i = 0; i < lead; i++) grid.appendChild(el('span', 'rsv-day is-blank'));

        for (let d = 1; d <= days; d++) {
            const value = iso(y, m, d);
            const btn = el('button', 'rsv-day', String(d));
            btn.type = 'button';
            btn.disabled = !dateSet.has(value);
            if (value === todayIso) btn.classList.add('is-today');
            if (value === state.date) { btn.classList.add('is-selected'); btn.setAttribute('aria-pressed', 'true'); }
            btn.setAttribute('aria-label', fmtDate(value) + (btn.disabled ? ' (not available)' : ''));
            btn.addEventListener('click', () => pickDate(value));
            grid.appendChild(btn);
        }

        const dates = cfg.dates.map(parseDate);
        const min = dates[0], max = dates[dates.length - 1];
        $('rsvCalPrev').disabled = !min || (y === min.getFullYear() && m <= min.getMonth()) || y < min.getFullYear();
        $('rsvCalNext').disabled = !max || (y === max.getFullYear() && m >= max.getMonth()) || y > max.getFullYear();
    }

    function pickDate(value) {
        if (state.date === value) return;
        state.date = value;
        state.time = null;
        state.timeLabel = null;
        state.custom = false;
        state.overlap = false;
        closePop();
        touch();
        renderCalendar();
        updateSummary();
        updateNext1();
        loadSlots();
    }

    async function loadSlots() {
        const box = $('rsvSlots');
        if (!state.date) { box.replaceChildren(el('p', 'rsv-empty', 'Pick a date first.')); return; }

        const mine = ++slotRequest;
        box.replaceChildren(...Array.from({ length: 6 }, () => el('span', 'rsv-skeleton')));

        try {
            const res = await fetch(`?handler=Slots&date=${encodeURIComponent(state.date)}`, { headers: { Accept: 'application/json' } });
            const json = await res.json();
            if (mine !== slotRequest) return;          // a newer date was picked meanwhile
            renderSlots(json.slots || []);
        } catch {
            if (mine !== slotRequest) return;
            box.replaceChildren(el('p', 'rsv-empty', 'Could not load times. Please pick the date again.'));
        }
    }

    function renderSlots(slots) {
        const box = $('rsvSlots');
        box.replaceChildren();

        if (slots.length === 0) {
            box.appendChild(el('p', 'rsv-empty', 'No times left on this date. Please try another day.'));
            state.time = null; state.timeLabel = null;
        } else {
            const stillThere = slots.find(s => s.time === state.time);
            if (stillThere) { state.timeLabel = stillThere.label; state.custom = false; }
            else if (!state.custom) { state.time = null; state.timeLabel = null; }

            slots.forEach(s => {
                const btn = el('button', 'rsv-slot', s.label);
                btn.type = 'button';
                btn.setAttribute('role', 'radio');
                const on = s.time === state.time;
                btn.setAttribute('aria-checked', String(on));
                if (on) btn.classList.add('is-selected');
                btn.addEventListener('click', () => {
                    state.time = s.time; state.timeLabel = s.label;
                    state.custom = false;
                    syncOther();
                    touch();
                    box.querySelectorAll('.rsv-slot').forEach(b => { b.classList.remove('is-selected'); b.setAttribute('aria-checked', 'false'); });
                    btn.classList.add('is-selected'); btn.setAttribute('aria-checked', 'true');
                    updateSummary(); updateNext1();
                    checkOverlap();
                    syncPopToTime();
                });
                box.appendChild(btn);
            });
        }
        syncOther();
        updateSummary();
        updateNext1();
        checkOverlap();
    }

    // ---------- step 1: "Other time" (any 5 minutes) ----------
    const STEP = 5;
    const pop = { total: 18 * 60, request: 0, timer: 0, ok: false };   // minutes after midnight

    const hhmm = t => String(Math.floor(t / 60)).padStart(2, '0') + ':' + String(t % 60).padStart(2, '0');
    function label12(t) {
        const h = Math.floor(t / 60), m = t % 60;
        return `${h % 12 === 0 ? 12 : h % 12}:${String(m).padStart(2, '0')} ${h >= 12 ? 'PM' : 'AM'}`;
    }

    function syncOther() {
        const btn = $('rsvOtherBtn');
        btn.disabled = !state.date;
        btn.classList.toggle('is-selected', state.custom && !!state.time);
        $('rsvOtherLabel').textContent = state.custom && state.timeLabel ? state.timeLabel : 'Other time';
        if (state.custom) {
            document.querySelectorAll('#rsvSlots .rsv-slot').forEach(b => { b.classList.remove('is-selected'); b.setAttribute('aria-checked', 'false'); });
        }
    }

    function renderPop() {
        const h = Math.floor(pop.total / 60);
        $('rsvHourVal').textContent = String(h % 12 === 0 ? 12 : h % 12);
        $('rsvMinVal').textContent = String(pop.total % 60).padStart(2, '0');
        $('rsvAmPm').textContent = h >= 12 ? 'PM' : 'AM';
        $('rsvPopUse').textContent = `Use ${label12(pop.total)}`;
    }

    function setPopStatus(kind, text) {
        const box = $('rsvPopStatus');
        box.className = 'rsv-pop-status' + (kind ? ' is-' + kind : '');
        box.textContent = text || '';
    }

    function movePop(delta) {
        pop.total = (pop.total + delta + 1440) % 1440;
        renderPop();
        scheduleCheck();
    }

    function scheduleCheck() {
        pop.ok = false;
        $('rsvPopUse').disabled = true;
        $('rsvPopSug').hidden = true;
        setPopStatus('wait', 'Checking tables...');
        window.clearTimeout(pop.timer);
        pop.timer = window.setTimeout(checkPopTime, reduceMotion ? 0 : 350);
    }

    async function checkPopTime() {
        const mine = ++pop.request;
        try {
            const url = `?handler=CheckTime&date=${encodeURIComponent(state.date)}&time=${encodeURIComponent(hhmm(pop.total))}&guests=${state.guests}`;
            const res = await fetch(url, { headers: { Accept: 'application/json' } });
            const json = await res.json();
            if (mine !== pop.request) return;
            showPopResult(json);
        } catch {
            if (mine !== pop.request) return;
            setPopStatus('bad', 'Could not check this time. Please try again.');
        }
    }

    function showPopResult(r) {
        const list = $('rsvPopSugList');
        list.replaceChildren();
        pop.ok = !!(r.valid && r.available);
        $('rsvPopUse').disabled = !pop.ok;

        if (pop.ok) {
            setPopStatus('ok', `Tables available at ${label12(pop.total)}`);
            $('rsvPopSug').hidden = true;
            return;
        }
        setPopStatus('bad', r.message || 'This time is not available.');

        const sugg = r.suggestions || [];
        $('rsvPopSug').hidden = sugg.length === 0;
        sugg.forEach(s => {
            const btn = el('button', 'rsv-sug');
            btn.type = 'button';
            const kind = s.kind === 'earlier' ? 'Earlier' : s.kind === 'later' ? 'Later' : 'Next day';
            const text = s.kind === 'nextday' ? `${fmtDate(s.date).replace(/, \d{4}$/, '')} \u00B7 ${s.label}` : s.label;
            btn.append(el('strong', '', text), el('small', '', kind));
            btn.addEventListener('click', () => applySuggestion(s));
            list.appendChild(btn);
        });
    }

    function applySuggestion(s) {
        if (s.date !== state.date) {
            // another day: the time is kept through the date change
            state.date = s.date;
            state.view = { y: parseDate(s.date).getFullYear(), m: parseDate(s.date).getMonth() };
            renderCalendar();
            loadSlots();
        }
        chooseTime(s.time, s.label, true);
        closePop(true);
    }

    function chooseTime(time, labelText, custom) {
        state.time = time; state.timeLabel = labelText; state.custom = custom;
        // a 30-minute button is highlighted when the time matches one
        const match = Array.from(document.querySelectorAll('#rsvSlots .rsv-slot')).find(b => b.textContent === labelText);
        if (match) state.custom = false;
        touch();
        syncOther();
        document.querySelectorAll('#rsvSlots .rsv-slot').forEach(b => {
            const on = b.textContent === labelText && !state.custom;
            b.classList.toggle('is-selected', on); b.setAttribute('aria-checked', String(on));
        });
        updateSummary(); updateNext1();
        checkOverlap();
    }

    // If the "Other time" box is open while a 30-minute button is picked, it follows that button.
    function syncPopToTime() {
        if ($('rsvPop').hidden || !state.time) return;
        pop.total = parseInt(state.time.slice(0, 2), 10) * 60 + parseInt(state.time.slice(3), 10);
        renderPop();
        scheduleCheck();
    }

    function openPop() {
        if (!state.date) return;
        const start = state.time ? parseInt(state.time.slice(0, 2), 10) * 60 + parseInt(state.time.slice(3), 10) : pop.total;
        pop.total = Math.round(start / STEP) * STEP % 1440;
        $('rsvPop').hidden = false;
        $('rsvOtherBtn').setAttribute('aria-expanded', 'true');
        renderPop();
        scheduleCheck();
        $('rsvPop').scrollIntoView({ block: 'nearest', behavior: reduceMotion ? 'auto' : 'smooth' });
        $('rsvPopUse').focus({ preventScroll: true });
    }

    function closePop(skipFocus) {
        const box = $('rsvPop');
        if (box.hidden) return;
        box.hidden = true;
        window.clearTimeout(pop.timer);
        pop.request++;
        $('rsvOtherBtn').setAttribute('aria-expanded', 'false');
        if (!skipFocus) $('rsvOtherBtn').focus({ preventScroll: true });
    }

    function initPop() {
        $('rsvOtherBtn').addEventListener('click', () => { if ($('rsvPop').hidden) openPop(); else closePop(); });
        $('rsvPopClose').addEventListener('click', () => closePop());
        $('rsvHourUp').addEventListener('click', () => movePop(60));
        $('rsvHourDown').addEventListener('click', () => movePop(-60));
        $('rsvMinUp').addEventListener('click', () => movePop(STEP));
        $('rsvMinDown').addEventListener('click', () => movePop(-STEP));
        $('rsvAmPm').addEventListener('click', () => movePop(720));
        $('rsvPopUse').addEventListener('click', () => {
            if (!pop.ok) return;
            chooseTime(hhmm(pop.total), label12(pop.total), true);
            closePop();
        });
        $('rsvPop').addEventListener('keydown', e => {
            if (e.key === 'Escape') { e.preventDefault(); e.stopPropagation(); closePop(); }
        });
        document.addEventListener('click', e => {
            if ($('rsvPop').hidden) return;
            if (!$('rsvTimeCard').contains(e.target)) closePop(true);
        });
    }

    function setGuests(n) {
        n = Math.max(1, Math.min(cfg.maxGuests, n));
        const changed = n !== state.guests;
        state.guests = n;
        if (changed) { touch(); if (!$('rsvPop').hidden) scheduleCheck(); }
        renderGuests(true);
        updateSummary();
        updateNext1();
    }

    // Typing: accept digits only. Empty or out-of-range is shown, never silently changed until the field loses focus.
    function onGuestsTyping() {
        const input = $('rsvGuests');
        const digits = input.value.replace(/\D/g, '').slice(0, 3);
        if (digits !== input.value) input.value = digits;
        const n = digits === '' ? NaN : parseInt(digits, 10);
        const ok = Number.isInteger(n) && n >= 1 && n <= cfg.maxGuests;
        input.classList.toggle('is-bad', !ok);
        const hint = $('rsvGuestsHint');
        hint.classList.toggle('is-bad', !ok);
        if (ok) {
            if (n !== state.guests) { state.guests = n; touch(); updateSummary(); }
            hint.textContent = `Up to ${cfg.maxGuests} guests per booking.`;
        } else {
            hint.textContent = digits === ''
                ? 'Enter the number of guests.'
                : n < 1 ? 'At least 1 guest.'
                    : `Our largest table seats ${cfg.maxGuests}. For bigger parties, please contact the restaurant.`;
        }
        $('rsvGuestsDown').disabled = !ok || n <= 1;
        $('rsvGuestsUp').disabled = !ok || n >= cfg.maxGuests;
        $('rsvNext1').disabled = !(ok && state.date && state.time) || state.overlap;
    }

    function onGuestsCommit() {
        const n = parseInt($('rsvGuests').value, 10);
        setGuests(Number.isInteger(n) ? n : state.guests);
    }
    function renderGuests(force) {
        const input = $('rsvGuests');
        if (force || document.activeElement !== input) input.value = String(state.guests);
        input.classList.remove('is-bad');
        $('rsvGuestsHint').classList.remove('is-bad');
        $('rsvGuestsDown').disabled = state.guests <= 1;
        $('rsvGuestsUp').disabled = state.guests >= cfg.maxGuests;
        $('rsvGuestsHint').textContent = `Up to ${cfg.maxGuests} guests per booking.`;
    }

    function updateNext1() {
        $('rsvNext1').disabled = !(state.date && state.time && state.guests >= 1) || state.overlap;
    }

    // The customer's own booking at the same time is found as soon as the time is chosen, not at the last step.
    let overlapRequest = 0;
    let overlapShown = false;

    async function checkOverlap() {
        const mine = ++overlapRequest;
        const clear = () => {
            state.overlap = false;
            if (overlapShown) { showError(1, ''); showError(2, ''); overlapShown = false; }
            updateNext1();
        };
        if (!state.date || !state.time) { clear(); return false; }

        try {
            const url = `?handler=Overlap&date=${encodeURIComponent(state.date)}&time=${encodeURIComponent(state.time)}`;
            const res = await fetch(url, { headers: { Accept: 'application/json' } });
            const json = await res.json();
            if (mine !== overlapRequest) return state.overlap;
            if (json.overlap) {
                state.overlap = true;
                overlapShown = true;
                showError(state.step === 2 ? 2 : 1, json.message || 'You already have a reservation at this time.');
                updateNext1();
                return true;
            }
            clear();
        } catch {
            if (mine === overlapRequest) clear();   // the server checks again on submit
        }
        return false;
    }

    // ---------- step 2: table types ----------
    const STATUS_TEXT = { available: 'Available', fullybooked: 'Fully booked', notsuitable: 'Not suitable', unavailable: 'Unavailable' };

    function seatsText(o) {
        return o.minGuests === o.maxGuests
            ? `Seats ${plural(o.maxGuests, 'guest', 'guests')}`
            : `Seats ${o.minGuests}–${o.maxGuests} guests`;
    }

    function buildTypeCard(o, index) {
        const usable = o.status === 'available';
        const card = el('button', 'rsv-type');
        card.type = 'button';
        card.style.setProperty('--i', index);
        card.setAttribute('role', 'radio');
        card.setAttribute('aria-checked', String(state.typeId === o.id));
        card.setAttribute('aria-disabled', String(!usable));
        card.dataset.id = o.id;
        if (state.typeId === o.id && usable) card.classList.add('is-selected');

        const photo = el('div', 'rsv-type-photo');
        if (o.photo) {
            const img = document.createElement('img');
            img.src = o.photo; img.alt = ''; img.loading = 'lazy';
            img.addEventListener('error', () => { img.remove(); photo.appendChild(noPhoto()); });
            photo.appendChild(img);
        } else {
            photo.appendChild(noPhoto());
        }
        const tick = el('span', 'rsv-type-check'); tick.innerHTML = '<i class="bi bi-check-lg" aria-hidden="true"></i>';
        photo.appendChild(tick);
        card.appendChild(photo);

        // status sits on the photo, so the text below stays short
        photo.appendChild(el('span', 'rsv-status rsv-status-pin ' + o.status, STATUS_TEXT[o.status] || o.status));

        const body = el('div', 'rsv-type-body');

        const head = el('div', 'rsv-type-head');
        head.appendChild(el('h3', 'rsv-type-name', o.name));
        if (o.japaneseName) { const jp = el('span', 'rsv-type-jp', o.japaneseName); jp.lang = 'ja'; head.appendChild(jp); }
        body.appendChild(head);

        // one line: dining area and party size
        const meta = el('p', 'rsv-type-meta');
        const metaItem = (icon, text, title) => {
            const item = el('span', 'rsv-type-meta-item');
            item.title = title;
            const ic = document.createElement('i');
            ic.className = 'bi bi-' + icon; ic.setAttribute('aria-hidden', 'true');
            item.append(ic, document.createTextNode(text));
            return item;
        };
        if (o.areaName) {
            const areaItem = el('span', 'rsv-type-meta-item');
            areaItem.title = 'Dining area';
            areaItem.append(areaIcon(o.areaIcon), document.createTextNode(o.areaName));
            meta.appendChild(areaItem);
        }
        meta.appendChild(metaItem('people', o.minGuests === o.maxGuests
            ? plural(o.maxGuests, 'guest', 'guests')
            : `${o.minGuests}\u2013${o.maxGuests} guests`, 'Seats'));
        body.appendChild(meta);

        if (o.description) body.appendChild(el('p', 'rsv-type-desc', o.description));

        // footer: minimum spend on the left, availability on the right
        const foot = el('div', 'rsv-type-foot');
        foot.appendChild(el('span', 'rsv-type-spend', o.minimumSpend > 0 ? `Min. spend ${fmtMoney(o.minimumSpend)}` : 'No minimum spend'));
        let line = o.reason;
        if (usable) line = o.tablesLeft <= 2 ? `Only ${o.tablesLeft} left` : `${o.tablesLeft} tables left`;
        if (line) foot.appendChild(el('span', 'rsv-type-reason', line));
        body.appendChild(foot);
        if (usable && o.belowMinimum) body.appendChild(el('p', 'rsv-type-note', 'Smaller party: the minimum spend applies.'));

        // A fully booked type shows its own nearest free times; one press moves the booking there.
        if (o.status === 'fullybooked' && (o.suggestions || []).length) {
            const box = el('div', 'rsv-type-sugs');
            box.appendChild(el('span', 'rsv-type-sugs-title', 'Next available'));
            o.suggestions.forEach(s => {
                const kind = s.kind === 'earlier' ? 'Earlier' : s.kind === 'later' ? 'Later' : 'Next day';
                const text = s.kind === 'nextday' ? `${fmtDate(s.date).replace(/, \d{4}$/, '')} \u00B7 ${s.label}` : s.label;
                const chip = el('span', 'rsv-type-sug', text);
                chip.setAttribute('role', 'button');
                chip.setAttribute('aria-label', `${kind}: ${text}`);
                chip.tabIndex = 0;
                const go = e => { e.preventDefault(); e.stopPropagation(); moveBooking(s, o.id); };
                chip.addEventListener('click', go);
                chip.addEventListener('keydown', e => { if (e.key === 'Enter' || e.key === ' ') go(e); });
                box.appendChild(chip);
            });
            body.appendChild(box);
        }
        card.appendChild(body);
        card.addEventListener('click', () => { if (usable) pickType(o); });
        return card;
    }

    function noPhoto() {
        const box = el('div', 'rsv-nophoto');
        box.innerHTML = '<i class="bi bi-image" aria-hidden="true"></i>';
        box.appendChild(el('span', '', 'No photo'));
        return box;
    }

    // Step 2: take a suggested time (maybe on another day) and look at the table types again.
    function moveBooking(s, typeId) {
        if (state.busy) return;
        if (s.date !== state.date) {
            state.date = s.date;
            const d = parseDate(s.date);
            state.view = { y: d.getFullYear(), m: d.getMonth() };
            renderCalendar();
        }
        state.time = s.time; state.timeLabel = s.label; state.custom = false;
        // the table type that offered this time is chosen again once it is free there
        state.typeId = typeId; state.type = null;
        touch();
        loadSlots();   // step 1 shows the same date and time if the customer goes back
        $('rsvChipDate').textContent = fmtDate(state.date);
        $('rsvChipTime').textContent = state.timeLabel;
        $('rsvStepNote1').textContent = `${fmtDate(state.date)}, ${state.timeLabel}, ${plural(state.guests, 'guest', 'guests')}`;
        updateSummary();
        checkOverlap();
        loadTypes().then(() => {
            const chosen = document.querySelector('#rsvTypes .rsv-type.is-selected');
            if (chosen) chosen.scrollIntoView({ block: 'nearest', behavior: reduceMotion ? 'auto' : 'smooth' });
        });
    }

    function pickType(o) {
        if (state.typeId !== o.id) { touch(); }
        state.typeId = o.id;
        state.type = o;
        saveDraft();
        showError(2, '');
        document.querySelectorAll('#rsvTypes .rsv-type').forEach(card => {
            const on = Number(card.dataset.id) === o.id;
            card.classList.toggle('is-selected', on);
            card.setAttribute('aria-checked', String(on));
        });
        $('rsvNext2').disabled = false;
        updateSummary();
    }

    async function loadTypes() {
        const box = $('rsvTypes');
        const mine = ++typeRequest;
        showError(2, '');
        box.replaceChildren(...Array.from({ length: 3 }, () => el('div', 'rsv-card rsv-skeleton-card')));
        $('rsvNext2').disabled = true;

        try {
            const url = `?handler=Types&date=${encodeURIComponent(state.date)}&time=${encodeURIComponent(state.time)}&guests=${state.guests}`;
            const res = await fetch(url, { headers: { Accept: 'application/json' } });
            const json = await res.json();
            if (mine !== typeRequest) return;

            if (json.error) {
                box.replaceChildren();
                showError(2, json.error);
                state.type = null; state.typeId = null;
                updateSummary();
                return;
            }

            const options = json.options || [];
            const hadChoice = state.typeId !== null;
            const kept = options.find(o => o.id === state.typeId && o.status === 'available');

            if (kept) {
                // If the spend changed since the customer accepted it, ask again.
                if (state.type && state.type.minimumSpend !== kept.minimumSpend) state.accepted = false;
                state.type = kept;
            } else {
                if (hadChoice && options.length) showError(2, 'The table type you chose is no longer available for this time. Please choose again.');
                state.typeId = null; state.type = null;
            }

            box.replaceChildren();
            if (options.length === 0) {
                box.appendChild(el('p', 'rsv-empty', 'No table types are open for booking right now. Please try another time.'));
            }
            options.forEach((o, i) => box.appendChild(buildTypeCard(o, i)));

            if (!options.some(o => o.status === 'available')) {
                showError(2, 'Nothing is available for this date, time and party size. Try another time or day.');
            }

            $('rsvNext2').disabled = !state.type;
            updateSummary();
        } catch {
            if (mine !== typeRequest) return;
            box.replaceChildren();
            showError(2, 'Could not check availability. Please try again.');
        }
    }

    // ---------- step 3: review ----------
    // ---------- long text: tiles keep one size, "View" opens the full text ----------
    let modalReturnFocus = null;

    function ensureModal() {
        let modal = $('rsvModal');
        if (modal) return modal;

        modal = el('div', 'rsv-modal');
        modal.id = 'rsvModal';
        modal.hidden = true;
        modal.setAttribute('role', 'dialog');
        modal.setAttribute('aria-modal', 'true');
        modal.setAttribute('aria-labelledby', 'rsvModalTitle');

        const panel = el('div', 'rsv-modal-panel');
        const title = el('p', 'rsv-modal-title'); title.id = 'rsvModalTitle';
        const body = el('p', 'rsv-modal-body'); body.id = 'rsvModalBody';
        const actions = el('div', 'rsv-modal-actions');
        const close = el('button', 'rsv-btn rsv-btn-ghost', 'Close');
        close.type = 'button'; close.id = 'rsvModalClose';
        actions.appendChild(close);
        panel.append(title, body, actions);
        modal.appendChild(panel);

        close.addEventListener('click', closeFullText);
        modal.addEventListener('click', e => { if (e.target === modal) closeFullText(); });
        modal.addEventListener('keydown', e => {
            if (e.key === 'Escape') { e.preventDefault(); closeFullText(); }
            else if (e.key === 'Tab') { e.preventDefault(); close.focus(); }   // only one control inside
        });

        $('rsv').appendChild(modal);
        return modal;
    }

    function openFullText(label, text, opener) {
        const modal = ensureModal();
        $('rsvModalTitle').textContent = label;
        $('rsvModalBody').textContent = text;          // textContent: customer text is never parsed as HTML
        modalReturnFocus = opener || null;
        modal.hidden = false;
        void modal.offsetWidth;
        modal.classList.add('is-open');
        $('rsvModalClose').focus();
    }

    function closeFullText() {
        const modal = $('rsvModal');
        if (!modal || modal.hidden) return;
        modal.classList.remove('is-open');
        setTimeout(() => { modal.hidden = true; }, reduceMotion ? 0 : 350);
        if (modalReturnFocus) modalReturnFocus.focus();
    }

    // Adds a "View" button to every review tile whose text does not fit on one line.
    function markCutOff() {
        document.querySelectorAll('#rsvReview .rsv-rv-tile').forEach(tile => {
            const old = tile.querySelector('.rsv-rv-more');
            if (old) old.remove();

            const strong = tile.querySelector('strong');
            if (!strong || strong.scrollWidth <= strong.clientWidth + 1) return;

            const btn = el('button', 'rsv-rv-more');
            btn.type = 'button';
            const ic = document.createElement('i');
            ic.className = 'bi bi-arrows-angle-expand'; ic.setAttribute('aria-hidden', 'true');
            btn.append(ic, document.createTextNode('View'));
            btn.setAttribute('aria-label', 'View full ' + (tile.dataset.label || 'text'));
            btn.addEventListener('click', () => openFullText(tile.dataset.label || '', tile.dataset.full || '', btn));
            tile.appendChild(btn);
        });
    }
    function renderReview() {
        const rv = $('rsvReview');
        rv.replaceChildren();

        // icons are fixed markup; every piece of customer or server text goes in with textContent
        const withIcon = (name, text) => {
            const wrap = el('span');
            const ic = document.createElement('i');
            ic.className = 'bi bi-' + name; ic.setAttribute('aria-hidden', 'true');
            wrap.append(ic, document.createTextNode(text));
            return wrap;
        };

        const hero = el('div', 'rsv-rv-hero');
        hero.append(el('p', 'rsv-rv-eyebrow', 'Your evening'), el('p', 'rsv-rv-date', fmtDate(state.date)));
        const meta = el('p', 'rsv-rv-meta');
        meta.append(withIcon('clock', state.timeLabel), withIcon('people', plural(state.guests, 'guest', 'guests')));
        hero.appendChild(meta);
        rv.appendChild(hero);

        const grid = el('div', 'rsv-rv-grid');
        const tile = (icon, label, value, sub, wide, area) => {
            const box = el('div', 'rsv-rv-tile' + (wide ? ' is-wide' : ''));
            const ic = document.createElement('i');
            ic.className = 'bi bi-' + icon; ic.setAttribute('aria-hidden', 'true');
            const strong = el('strong', '', value);
            strong.title = value;
            box.append(ic, el('small', '', label), strong);
            if (sub || area) {
                // the Japanese name and the dining area share one line under the value
                const line = el('span', 'rsv-rv-sub');
                if (sub) { const j = el('span', '', sub); j.lang = 'ja'; line.appendChild(j); }
                if (area) {
                    const a = el('span', 'rsv-area-line');
                    a.append(areaIcon(area.icon), document.createTextNode(area.name));
                    line.appendChild(a);
                }
                box.appendChild(line);
            }
            box.dataset.label = label;
            box.dataset.full = value;
            grid.appendChild(box);
        };
        tile('grid-3x3-gap', 'Table type', state.type.name, state.type.japaneseName, false,
            state.type.areaName ? { name: state.type.areaName, icon: state.type.areaIcon } : null);
        tile('card-text', 'Special requests', state.special.trim() || 'None added', '', false);
        tile('person', 'Name', cfg.name || '-', '', false);
        tile('telephone', 'Phone', cfg.phone || 'Not on file', '', false);
        rv.appendChild(grid);
        markCutOff();

        const spend = state.type.minimumSpend;
        $('rsvSpendBox').hidden = !(spend > 0);
        $('rsvNoSpend').hidden = spend > 0;
        if (spend > 0) {
            $('rsvSpendAmount').textContent = fmtMoney(spend);
            $('rsvSpendSmall').textContent = state.type.belowMinimum
                ? `, even if you are only ${plural(state.guests, 'guest', 'guests')}` : '';
            $('rsvSpendAccept').checked = state.accepted;
        }
        updateSubmit();
    }

    function updateSubmit() {
        const needs = state.type && state.type.minimumSpend > 0;
        $('rsvSubmit').disabled = state.busy || (needs && !state.accepted);
    }

    async function submit() {
        if (state.busy || !state.type) return;
        if (state.type.minimumSpend > 0 && !state.accepted) return;

        state.busy = true;
        if (!state.token) state.token = newToken();
        const btn = $('rsvSubmit');
        btn.classList.add('is-loading');
        btn.disabled = true;
        showError(3, '');
        updateSummary();

        try {
            const res = await fetch('?handler=Submit', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    Accept: 'application/json',
                    RequestVerificationToken: document.querySelector('input[name="__RequestVerificationToken"]').value
                },
                body: JSON.stringify({
                    date: state.date,
                    time: state.time,
                    guests: state.guests,
                    tableTypeId: state.typeId,
                    specialRequest: state.special.trim(),
                    acceptedMinimumSpend: state.accepted,
                    expectedMinimumSpend: state.type.minimumSpend,
                    token: state.token
                })
            });
            const json = await res.json().catch(() => null);

            if (json && json.success) {
                await showTicket(json.ticket);
                return;
            }
            await handleFailure(json, res.status);
        } catch {
            showError(3, 'Could not reach the server. Your entries are still here, please try again.');
        } finally {
            state.busy = false;
            btn.classList.remove('is-loading');
            updateSubmit();
            updateSummary();
        }
    }

    async function handleFailure(json, status) {
        if (!json) {
            showError(3, status === 400 ? 'Your session may have expired. Please refresh the page and try again.' : 'Something went wrong. Please try again.');
            return;
        }
        const message = json.error || 'Unable to save your reservation.';

        switch (json.code) {
            case 'slot':
            case 'guests':
            case 'overlap':
            case 'limit':
                state.token = null;
                await goStep(1, true);
                showError(1, message);
                loadSlots();
                break;
            case 'type':
            case 'full':
            case 'spend':
                state.accepted = false;
                state.token = null;
                await goStep(2, true);
                showError(2, message);
                break;
            default:
                if (json.code === 'busy') { /* same token can be retried safely */ }
                showError(3, message);
        }
    }

    // ---------- the ticket ----------
    // The summary card turns over and travels to the middle in one move, while the side columns fade away.
    const FULL_WEEK = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];

    async function showTicket(t) {
        clearDraft();

        const spend = Number(t.minimumSpend) || 0;
        const d = state.date ? parseDate(state.date) : null;

        $('tkName').textContent = (cfg.name || '').split(' ')[0] || 'guest';
        $('tkRef').textContent = t.reference || '-';
        // The PDF page only accepts upcoming Pending / Confirmed bookings, which a new booking always is.
        const pdf = $('tkPdf');
        pdf.hidden = !(t.reservationId > 0 && ['Pending', 'Confirmed'].includes(t.status));
        if (!pdf.hidden) pdf.href = '/Customer/ReservationConfirmation?id=' + encodeURIComponent(t.reservationId);
        $('tkRefBox').classList.toggle('is-long', String(t.reference || '').length > 11);   // a long reference stays on one line
        $('tkStatus').textContent = t.status || 'Pending';
        $('tkDay').textContent = d ? String(d.getDate()) : '-';
        $('tkMonth').textContent = d ? `${MONTHS[d.getMonth()].slice(0, 3)} ${d.getFullYear()}` : (t.date || '-');
        $('tkWeekday').textContent = d ? FULL_WEEK[d.getDay()] : '';
        $('tkTime').textContent = t.time || '-';
        $('tkGuests').textContent = plural(t.guests, 'guest', 'guests');
        $('tkType').textContent = t.typeName || '-';
        $('tkTypeJp').textContent = t.typeJapaneseName || '';
        $('tkArea').textContent = t.area || '';
        $('tkAreaLine').hidden = !t.area;
        $('tkAreaIco').setAttribute('href', areaIconHref(t.areaIcon));
        $('tkSpecial').textContent = t.special || '';
        $('tkSpecial').title = t.special || '';
        $('tkSpecialRow').hidden = !t.special;
        $('tkSpendLabel').textContent = spend > 0 ? 'Minimum spend \u00B7 per booking' : 'Minimum spend';
        $('tkSpend').textContent = spend > 0 ? fmtMoney(spend) : 'None';
        $('tkAccepted').hidden = !(spend > 0);

        $('rsvBadge').textContent = t.status || 'Pending';
        $('rsvTicket').setAttribute('aria-hidden', 'false');

        const shell = $('rsvShell'), wrap = $('rsvSummaryWrap'), flip = $('rsvFlip');
        const front = flip.querySelector('.rsv-front');
        document.querySelectorAll('#rsvSteps li').forEach(li => {
            li.classList.remove('is-active');
            li.classList.add('is-done');
        });

        // the pass is 560 high; the card must be that tall before it starts to turn
        flip.style.minHeight = '560px';

        // where the card ends up: the middle of the shell, at its top
        const s = shell.getBoundingClientRect(), c = wrap.getBoundingClientRect();
        const dx = (s.left + s.width / 2) - (c.left + c.width / 2);
        const dy = s.top - c.top;

        const land = () => {
            // the centred layout puts the card exactly where the flight ended, so nothing jumps
            flip.style.transition = 'none';
            flip.classList.add('is-flipped');
            shell.classList.add('is-done');
        };

        shell.classList.add('is-fading');
        if (reduceMotion || !flip.animate) {
            land();
        } else {
            const flight = flip.animate([
                { transform: 'translate3d(0, 0, 0) rotateY(0deg)', offset: 0 },
                { transform: `translate3d(${dx * .5}px, ${dy * .5 - 22}px, 160px) rotateY(90deg)`, offset: .5, easing: 'cubic-bezier(.3, .7, .3, 1)' },
                { transform: `translate3d(${dx}px, ${dy}px, 0) rotateY(180deg)`, offset: 1 }
            ], { duration: 1700, easing: 'cubic-bezier(.45, .05, .2, 1)', fill: 'forwards' });
            await flight.finished.catch(() => { });
            land();
            flight.cancel();
        }
        void flip.offsetWidth;
        flip.style.transition = '';
        window.scrollTo({ top: 0, behavior: reduceMotion ? 'auto' : 'smooth' });

        front.setAttribute('aria-hidden', 'true');
        front.inert = true;
        $('tkTitle').focus({ preventScroll: true });
    }

    // ---------- moving between steps ----------
    async function goStep(n, back = false) {
        if (n >= 2 && !(state.date && state.time)) return;
        if (n >= 3 && !state.type) n = 2;

        state.step = n;
        $('rsvShell').dataset.step = String(n);

        document.querySelectorAll('.rsv-panel').forEach(panel => {
            const on = Number(panel.dataset.panel) === n;
            panel.hidden = !on;
            panel.classList.remove('is-current', 'is-back');
            if (on) { void panel.offsetWidth; panel.classList.add('is-current'); if (back) panel.classList.add('is-back'); }
        });

        document.querySelectorAll('#rsvSteps li').forEach(li => {
            const idx = Number(li.dataset.stepItem);
            li.classList.toggle('is-done', idx < n);
            li.classList.toggle('is-active', idx === n);
        });
        $('rsvSteps').style.setProperty('--progress-ratio', String((n - 1) / 2));

        $('rsvStepNote1').textContent = n > 1 && state.date
            ? `${fmtDate(state.date)}, ${state.timeLabel}, ${plural(state.guests, 'guest', 'guests')}` : 'Pick a date, time and party size.';
        $('rsvStepNote2').textContent = n > 2 && state.type ? state.type.name : 'Choose your setting.';

        if (n === 2) {
            $('rsvChipDate').textContent = fmtDate(state.date);
            $('rsvChipTime').textContent = state.timeLabel;
            $('rsvChipGuests').textContent = plural(state.guests, 'guest', 'guests');
            await loadTypes();
        }
        if (n === 3) renderReview();

        updateSummary();
        const title = document.querySelector(`#rsvStep${n} .rsv-title`);
        if (title) { title.tabIndex = -1; title.focus({ preventScroll: true }); }
        // Only scroll when the booking area is partly off screen; otherwise the page would jump on every step.
        if ($('rsv').getBoundingClientRect().top < -40) {
            $('rsv').scrollIntoView({ behavior: reduceMotion ? 'auto' : 'smooth', block: 'start' });
        }
    }

    // ---------- wire up ----------
    function init() {
        if (cfg.dates.length === 0) {
            $('rsvCalendar').replaceChildren(el('p', 'rsv-empty', 'Online reservations are not open right now. Please contact the restaurant.'));
            $('rsvNext1').disabled = true;
            return;
        }

        loadDraft();
        applyQueryPrefill();
        initView();
        renderCalendar();
        renderGuests();
        initPop();
        $('rsvSpecial').value = state.special;
        $('rsvSpecialCount').textContent = state.special.length;
        if (state.date) loadSlots();
        $('rsvSteps').style.setProperty('--progress-ratio', '0');
        updateSummary();
        updateNext1();

        $('rsvCalPrev').addEventListener('click', () => { state.view = { y: state.view.m === 0 ? state.view.y - 1 : state.view.y, m: (state.view.m + 11) % 12 }; renderCalendar(); });
        $('rsvCalNext').addEventListener('click', () => { state.view = { y: state.view.m === 11 ? state.view.y + 1 : state.view.y, m: (state.view.m + 1) % 12 }; renderCalendar(); });
        $('rsvGuestsDown').addEventListener('click', () => setGuests(state.guests - 1));
        $('rsvGuestsUp').addEventListener('click', () => setGuests(state.guests + 1));
        $('rsvGuests').addEventListener('input', onGuestsTyping);
        $('rsvGuests').addEventListener('change', onGuestsCommit);
        $('rsvGuests').addEventListener('blur', onGuestsCommit);
        $('rsvGuests').addEventListener('focus', e => e.target.select());
        $('rsvGuests').addEventListener('keydown', e => {
            if (e.key === 'Enter') { e.preventDefault(); onGuestsCommit(); }
            else if (e.key === 'ArrowUp') { e.preventDefault(); setGuests(state.guests + 1); }
            else if (e.key === 'ArrowDown') { e.preventDefault(); setGuests(state.guests - 1); }
        });

        $('rsvNext1').addEventListener('click', () => { showError(1, ''); goStep(2); });
        $('rsvNext2').addEventListener('click', () => { if (state.type) goStep(3); });
        $('rsvSubmit').addEventListener('click', submit);

        document.querySelectorAll('[data-goto]').forEach(b => b.addEventListener('click', () => {
            if (state.busy) return;
            const n = Number(b.dataset.goto);
            goStep(n, n < state.step);
        }));

        $('rsvSpecial').addEventListener('input', e => {
            state.special = e.target.value;
            $('rsvSpecialCount').textContent = state.special.length;
            state.token = null;
            saveDraft();
            updateSummary();
        });

        $('rsvSpendAccept').addEventListener('change', e => { state.accepted = e.target.checked; updateSubmit(); });

        window.addEventListener('resize', () => { if (state.step === 3) markCutOff(); });
    }

    init();
})();