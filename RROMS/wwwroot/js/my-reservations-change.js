// Owner: Member 2 — US15 modify my reservation. Server/customer strings always use textContent.
(() => {
    'use strict';
    const page = document.getElementById('myReservations');
    const dlg = document.getElementById('myrChange');
    if (!page || !dlg) return;
    const byId = id => document.getElementById(id);
    const cfg = JSON.parse(byId('myrConfig').textContent);
    const items = new Map(cfg.items.map(item => [Number(item.id), item]));
    const details = byId('myrDetails');
    const slotsBox = byId('myrChangeSlots');
    const calGrid = byId('myrCalGrid');
    const calTitle = byId('myrCalTitle');
    const calPrev = byId('myrCalPrev');
    const calNext = byId('myrCalNext');
    const leave = byId('myrChangeLeave');
    const dateBtn = byId('myrDateBtn');
    const timeBtn = byId('myrTimeBtn');
    const calPop = byId('myrCal');
    const timePop = byId('myrTimePop');
    let dateVal = '';        // the date picked in the calendar (yyyy-mm-dd)
    let timeVal = '';        // the time picked (HH:mm)
    let viewY = 0, viewM = 0;
    let allowed = new Set();
    const guestsOut = byId('myrChangeGuests');
    const minus = byId('myrChangeMinus');
    const plus = byId('myrChangePlus');
    const statusBox = byId('myrChangeStatus');
    const suggestBox = byId('myrChangeSuggest');
    const typesBox = byId('myrChangeTypes');
    const typeList = byId('myrChangeTypeList');
    const spendRow = byId('myrChangeSpendRow');
    const spendBox = byId('myrChangeSpend');
    const spendText = byId('myrChangeSpendText');
    const submit = byId('myrChangeSubmit');
    const keep = byId('myrChangeKeep');
    const requestBox = byId('myrChangeRequest');
    const requestCount = byId('myrChangeRequestCount');
    const clockOffset = cfg.serverNowMs - Date.now();
    const restaurantNow = () => Date.now() + clockOffset;
    let current = null;      // the booking being changed
    let guests = 2;
    let viewId = null;       // the booking last opened in the details pop-up
    let opener = null;
    let seq = 0;             // newer answers win over older ones
    let timer = 0;
    let busy = false;
    let verdict = null;      // last server answer for the chosen values
    let typeId = null;       // another table type picked because the party outgrew the booked one
    let typeOptions = [];    // the types the server offered

    const canChange = item => Boolean(item && item.canCancel &&
        ['Pending', 'Confirmed'].includes(item.status) && item.reservedAtMs > restaurantNow());

    // Same page-scroll lock as the other pop-ups on this page.
    const lock = on => {
        const root = document.documentElement;
        if (on && !root.classList.contains('myr-has-dialog')) {
            root.style.setProperty('--myr-sbw', Math.max(0, window.innerWidth - root.clientWidth) + 'px');
            root.classList.add('myr-has-dialog');
        } else if (!on && !details.open && !byId('myrConfirm').open) {
            root.classList.remove('myr-has-dialog');
            root.style.removeProperty('--myr-sbw');
        }
    };

    const dateLabel = iso => {
        const [y, m, d] = iso.split('-').map(Number);
        const text = new Date(y, m - 1, d, 12).toLocaleDateString('en-GB', { weekday: 'short', day: 'numeric', month: 'short' });
        const today = new Date(restaurantNow());
        const todayIso = today.getFullYear() + '-' + String(today.getMonth() + 1).padStart(2, '0') + '-' + String(today.getDate()).padStart(2, '0');
        return text + (iso === todayIso ? ' (today)' : '');
    };
    const clock12 = hhmm => {
        const [h, m] = hhmm.split(':').map(Number);
        return (h % 12 || 12) + ':' + String(m).padStart(2, '0') + ' ' + (h < 12 ? 'AM' : 'PM');
    };
    const option = (value, label) => { const o = document.createElement('option'); o.value = value; o.textContent = label; return o; };

    const pad = n => String(n).padStart(2, '0');
    const isoOf = (y, m, d) => y + '-' + pad(m + 1) + '-' + pad(d);

    function renderCal() {
        const first = new Date(viewY, viewM, 1);
        calTitle.textContent = first.toLocaleDateString('en-GB', { month: 'long', year: 'numeric' });
        const lead = (first.getDay() + 6) % 7;     // weeks start on Monday, like the reservation page
        const days = new Date(viewY, viewM + 1, 0).getDate();
        const todayD = new Date(restaurantNow());
        const todayIso = isoOf(todayD.getFullYear(), todayD.getMonth(), todayD.getDate());
        const cells = [];
        for (let i = 0; i < lead; i++) {
            const blank = document.createElement('span');
            blank.className = 'myr-cal-day is-blank';
            cells.push(blank);
        }
        for (let d = 1; d <= days; d++) {
            const iso = isoOf(viewY, viewM, d);
            const b = document.createElement('button');
            b.type = 'button';
            b.className = 'myr-cal-day' + (iso === todayIso ? ' is-today' : '') + (iso === dateVal ? ' is-selected' : '');
            b.textContent = String(d);
            b.dataset.date = iso;
            b.disabled = !allowed.has(iso);
            b.setAttribute('aria-pressed', String(iso === dateVal));
            b.setAttribute('aria-label', dateLabel(iso));
            cells.push(b);
        }
        calGrid.replaceChildren(...cells);
        const months = [...allowed].map(x => x.slice(0, 7)).sort();
        const here = viewY + '-' + pad(viewM + 1);
        calPrev.disabled = !months.length || here <= months[0];
        calNext.disabled = !months.length || here >= months[months.length - 1];
    }

    function showMonthOf(iso) {
        const [y, m] = iso.split('-').map(Number);
        viewY = y; viewM = m - 1;
        renderCal();
    }

    function setDate(iso) {
        dateVal = iso;
        byId('myrDateText').textContent = dateLabel(iso);
        showMonthOf(iso);
    }

    // The calendar and the time list open under their buttons, one at a time.
    function closePops() {
        [[calPop, dateBtn], [timePop, timeBtn]].forEach(([pop, btn]) => { pop.hidden = true; btn.setAttribute('aria-expanded', 'false'); });
    }
    function togglePop(pop, btn) {
        const wasOpen = !pop.hidden;
        closePops();
        if (wasOpen) return;
        pop.hidden = false;
        btn.setAttribute('aria-expanded', 'true');
        pop.querySelector('.is-selected')?.scrollIntoView({ block: 'nearest' });
    }
    dateBtn.addEventListener('click', () => togglePop(calPop, dateBtn));
    timeBtn.addEventListener('click', () => { if (!timeBtn.disabled) togglePop(timePop, timeBtn); });
    dlg.addEventListener('click', event => {
        if (!event.target.closest('.myr-change-field')) closePops();
    });

    function setTime(hhmm) {
        timeVal = hhmm;
        byId('myrTimeText').textContent = hhmm ? clock12(hhmm) : '-';
        slotsBox.querySelectorAll('.myr-slot').forEach(b => {
            const on = b.dataset.time === hhmm;
            b.classList.toggle('is-selected', on);
            b.setAttribute('aria-checked', String(on));
        });
    }

    async function fillTimes(date, selected) {
        slotsBox.setAttribute('aria-busy', 'true');
        timeBtn.disabled = true;
        byId('myrTimeText').textContent = 'Loading…';
        const wait = document.createElement('p');
        wait.className = 'myr-slots-empty';
        wait.textContent = 'Loading…';
        slotsBox.replaceChildren(wait);
        timeVal = '';
        let slots = [];
        try {
            const response = await fetch(cfg.slotsUrl + '&date=' + encodeURIComponent(date), {
                credentials: 'same-origin', headers: { 'Accept': 'application/json' }
            });
            if (response.ok) slots = (await response.json()).slots || [];
        } catch { /* the list stays empty: shown below */ }
        slotsBox.removeAttribute('aria-busy');
        const list = slots.map(s => [s.time, s.label]);
        // The current time may sit between the listed times (e.g. 7:05 PM): keep it selectable.
        if (date === current.dateIso && !list.some(x => x[0] === current.timeIso))
            list.push([current.timeIso, clock12(current.timeIso)]);
        list.sort((a, b) => a[0].localeCompare(b[0]));
        if (list.length === 0) {
            const none = document.createElement('p');
            none.className = 'myr-slots-empty';
            none.textContent = 'No times left on this date.';
            slotsBox.replaceChildren(none);
            byId('myrTimeText').textContent = 'No times';
            return false;
        }
        slotsBox.replaceChildren(...list.map(([v, l]) => {
            const b = document.createElement('button');
            b.type = 'button';
            b.className = 'myr-slot';
            b.dataset.time = v;
            b.setAttribute('role', 'radio');
            b.textContent = l;
            if (date === current.dateIso && v === current.timeIso) {
                const tag = document.createElement('small');
                tag.textContent = 'current';
                b.append(tag);
            }
            return b;
        }));
        timeBtn.disabled = false;
        setTime(list.some(x => x[0] === selected) ? selected : list[0][0]);
        return true;
    }

    function show(kind, text) {
        statusBox.hidden = !text;
        statusBox.className = 'myr-change-status' + (kind ? ' is-' + kind : '');
        statusBox.textContent = text || '';
    }

    function showSuggestions(list) {
        suggestBox.replaceChildren();
        suggestBox.hidden = !list || list.length === 0;
        if (suggestBox.hidden) return;
        const lead = document.createElement('span');
        lead.textContent = 'Free at:';
        suggestBox.append(lead);
        list.forEach(s => {
            const b = document.createElement('button');
            b.type = 'button';
            b.dataset.date = s.date;
            b.dataset.time = s.time;
            b.textContent = (s.date === dateVal ? '' : dateLabel(s.date) + ', ') + s.label;
            suggestBox.append(b);
        });
    }

    const money = n => 'RM ' + Number(n).toLocaleString('en-MY', { maximumFractionDigits: 0 });
    const chosenType = () => typeOptions.find(o => o.id === typeId) || null;

    function renderTypes() {
        typesBox.hidden = typeOptions.length === 0;
        typeList.replaceChildren();
        spendRow.hidden = true;
        if (typeOptions.length === 0) return;
        typeOptions.forEach(o => {
            const b = document.createElement('button');
            b.type = 'button';
            b.setAttribute('role', 'radio');
            b.setAttribute('aria-checked', String(o.id === typeId));
            b.dataset.type = String(o.id);
            b.className = 'myr-change-type' + (o.id === typeId ? ' is-on' : '');
            const name = document.createElement('strong');
            name.textContent = o.name + (o.japaneseName ? ' / ' + o.japaneseName : '');
            const info = document.createElement('span');
            info.textContent = o.minGuests + '–' + o.maxGuests + ' guests · ' +
                o.tablesLeft + (o.tablesLeft === 1 ? ' table left' : ' tables left');
            const spend = document.createElement('span');
            spend.className = 'myr-type-spend';
            spend.textContent = o.minimumSpend > 0 ? 'Min spend ' + money(o.minimumSpend) : 'No minimum spend';
            b.append(name, info, spend);
            typeList.append(b);
        });
        const c = chosenType();
        if (c && c.minimumSpend > 0) {
            spendRow.hidden = false;
            spendText.textContent = 'I accept the minimum spend of ' + money(c.minimumSpend) + ' per booking.';
        }
    }
    const spendOk = () => { const c = chosenType(); return !(c && c.minimumSpend > 0) || spendBox.checked; };
    function resetType() { typeId = null; typeOptions = []; spendBox.checked = false; renderTypes(); }

    const tidy = text => String(text || '').replace(/\r\n/g, '\n').trim();
    const requestChanged = () => tidy(requestBox.value) !== tidy(current.specialRequest);
    const scheduleSame = () => dateVal === current.dateIso && timeVal === current.timeIso && guests === current.guests && typeId === null;
    const unchanged = () => scheduleSame() && !requestChanged();

    async function check() {
        if (!current) return;
        const mine = ++seq;
        verdict = null;
        submit.disabled = true;
        showSuggestions(null);
        if (!timeVal) { show('bad', 'There are no times left on this date. Please choose another date.'); return; }
        if (unchanged()) { show('', 'This is your current booking. Pick a new date, time, number of guests or edit your request.'); return; }
        // Only the special request changed: nothing to check, the booking keeps its table and status.
        if (scheduleSame()) {
            verdict = { available: true, backToPending: false };
            show('ok', 'Your booking stays as it is. Our team will see your updated request.');
            submit.disabled = false;
            return;
        }
        // A new time (or table type) must be at least 1 hour ahead. Only adding or removing guests at the
        // same time is allowed closer than that. The server checks it again.
        const timeMoved = dateVal !== current.dateIso || timeVal !== current.timeIso;
        const at = new Date(dateVal + 'T' + timeVal + ':00').getTime();
        if ((timeMoved || typeId !== null) && at < restaurantNow() + cfg.minLeadMinutes * 60000 - 1000) {
            show('bad', 'Please choose a time at least 1 hour from now.');
            return;
        }
        show('wait', 'Checking tables…');
        const url = cfg.checkUrl + '&id=' + current.id + '&date=' + dateVal + '&time=' + timeVal +
            '&guests=' + guests + (typeId !== null ? '&typeId=' + typeId : '');
        try {
            const response = await fetch(url, { credentials: 'same-origin', headers: { 'Accept': 'application/json' } });
            if (mine !== seq) return;
            if (!response.ok) throw new Error('bad');
            const r = await response.json();
            if (mine !== seq) return;
            verdict = r;
            if (r.typeOptions && r.typeOptions.length) {
                // The party no longer fits the booked table type: let the customer pick another.
                typeOptions = r.typeOptions;
                if (!typeOptions.some(o => o.id === typeId)) typeId = null;
                renderTypes();
            } else if (typeId === null) {
                typeOptions = [];
                renderTypes();
            }
            if (r.available) {
                show('ok', r.backToPending
                    ? 'A table is free. Your booking will go back to Pending until our team confirms it.'
                    : 'You can keep your table. Your booking stays as it is.');
                submit.disabled = !spendOk();
            } else {
                show(r.typeOptions && r.typeOptions.length ? 'warn' : 'bad', r.message || 'That change is not available.');
                showSuggestions(r.suggestions);
            }
        } catch {
            if (mine === seq) show('bad', 'We could not check availability. Please try again.');
        }
    }
    const checkSoon = () => { clearTimeout(timer); timer = setTimeout(check, 120); };

    function setGuests(n, typing) {
        const wanted = n;
        guests = Math.max(1, Math.min(cfg.maxGuests, n));
        // Tell the customer why the + button stops (or a larger typed number was cut back).
        const limitHint = byId('myrChangeGuestsHint');
        if (limitHint) {
            const atLimit = wanted >= cfg.maxGuests;
            limitHint.hidden = !atLimit;
            limitHint.textContent = atLimit
                ? 'Our largest table seats ' + cfg.maxGuests + '. For bigger parties, please contact the restaurant.'
                : '';
        }
        if (current) resetType();
        guestsOut.max = String(cfg.maxGuests);
        if (!typing || Number(guestsOut.value) !== guests) guestsOut.value = String(guests);
        byId('myrChangeGuestsWord').textContent = guests === 1 ? 'guest' : 'guests';
        minus.disabled = guests <= 1;
        plus.disabled = guests >= cfg.maxGuests;
    }

    async function open(item, from) {
        if (!item || busy) return;
        if (!canChange(item)) return;
        current = item;
        opener = from || null;
        byId('myrChangeId').value = String(item.id);
        byId('myrChangeCurrent').textContent = item.date + ' · ' + item.time + ' · ' + item.guests + (item.guests === 1 ? ' guest' : ' guests');
        current = item;
        resetType();
        setGuests(item.guests);
        allowed = new Set(cfg.dates);
        allowed.add(item.dateIso);
        setDate(item.dateIso);
        timeVal = '';
        requestBox.value = item.specialRequest || '';
        requestCount.textContent = String(requestBox.value.length);
        leave.hidden = true;
        closePops();
        show('', '');
        showSuggestions(null);
        submit.disabled = true;
        submit.textContent = 'Save changes';
        dlg.showModal();
        lock(true);
        await fillTimes(item.dateIso, item.timeIso);
        check();
    }

    page.addEventListener('click', event => {
        const v = event.target.closest('[data-view]');
        if (v) { viewId = Number(v.dataset.view); return; }
        const c = event.target.closest('[data-change]');
        if (c) open(items.get(Number(c.dataset.change)), c);
    }, true);

    byId('myrOpenChange').addEventListener('click', () => {
        const item = items.get(viewId);
        if (!item) return;
        if (details.open) details.close();
        open(item, null);
    });
    // The details pop-up unlocks the page when it closes; keep it locked while this one is open.
    details.addEventListener('close', () => { if (dlg.open) lock(true); });

    calGrid.addEventListener('click', async event => {
        const b = event.target.closest('button[data-date]');
        if (!b || b.disabled || b.dataset.date === dateVal) return;
        resetType();
        setDate(b.dataset.date);
        closePops();
        submit.disabled = true;
        await fillTimes(dateVal, '');
        check();
    });
    const moveMonth = step => {
        const d = new Date(viewY, viewM + step, 1);
        viewY = d.getFullYear(); viewM = d.getMonth();
        renderCal();
    };
    calPrev.addEventListener('click', () => moveMonth(-1));
    calNext.addEventListener('click', () => moveMonth(1));
    slotsBox.addEventListener('click', event => {
        const b = event.target.closest('button[data-time]');
        if (!b) return;
        resetType();
        setTime(b.dataset.time);
        closePops();
        checkSoon();
    });
    // The number can also be typed. Empty is left alone until the box loses focus.
    guestsOut.addEventListener('input', () => {
        const raw = guestsOut.value.trim();
        if (!/^\d+$/.test(raw)) return;
        setGuests(Number(raw), true);
        checkSoon();
    });
    guestsOut.addEventListener('blur', () => { if (guestsOut.value !== String(guests)) { setGuests(guests); checkSoon(); } });
    guestsOut.addEventListener('keydown', event => { if (event.key === 'Enter') { event.preventDefault(); guestsOut.blur(); } });
    requestBox.addEventListener('input', () => {
        requestCount.textContent = String(requestBox.value.length);
        if (current && timeVal) checkSoon();
    });
    minus.addEventListener('click', () => { setGuests(guests - 1); checkSoon(); });
    plus.addEventListener('click', () => { setGuests(guests + 1); checkSoon(); });
    suggestBox.addEventListener('click', async event => {
        const b = event.target.closest('button[data-time]');
        if (!b) return;
        allowed.add(b.dataset.date);
        resetType();
        setDate(b.dataset.date);
        await fillTimes(b.dataset.date, b.dataset.time);
        check();
    });

    typeList.addEventListener('click', event => {
        const b = event.target.closest('button[data-type]');
        if (!b) return;
        typeId = Number(b.dataset.type);
        spendBox.checked = false;
        renderTypes();
        check();
    });
    spendBox.addEventListener('change', () => { submit.disabled = !(verdict && verdict.available && spendOk()); });

    // Closing with unsaved changes asks first: keep editing or discard.
    const dirty = () => Boolean(current && timeVal && !unchanged());
    function requestClose() {
        if (busy) return;
        if (!dirty()) { dlg.close(); return; }
        leave.hidden = false;
        byId('myrLeaveKeep').focus();
    }
    keep.addEventListener('click', requestClose);
    byId('myrChangeX').addEventListener('click', requestClose);
    byId('myrLeaveKeep').addEventListener('click', () => { leave.hidden = true; });
    byId('myrLeaveDiscard').addEventListener('click', () => { leave.hidden = true; if (!busy) dlg.close(); });
    dlg.addEventListener('cancel', event => {
        event.preventDefault();                 // Esc goes through the same question
        if (!leave.hidden) { leave.hidden = true; return; }
        if (!calPop.hidden || !timePop.hidden) { closePops(); return; }
        requestClose();
    });
    dlg.addEventListener('close', () => {
        lock(false);
        clearTimeout(timer);
        seq++;
        leave.hidden = true;
        if (opener?.isConnected && !opener.hidden) opener.focus({ preventScroll: true });
    });
    dlg.addEventListener('click', event => {
        const r = dlg.getBoundingClientRect();
        if (event.target === dlg &&
            (event.clientX < r.left || event.clientX > r.right || event.clientY < r.top || event.clientY > r.bottom)) requestClose();
    });

    byId('myrChangeForm').addEventListener('submit', async event => {
        event.preventDefault();
        if (busy || !current || submit.disabled) return;
        const token = byId('myrTokenForm').querySelector('input[name="__RequestVerificationToken"]');
        if (!token) { show('bad', 'Your session could not be verified. Refresh the page and try again.'); return; }
        busy = true;
        submit.disabled = true;
        keep.disabled = true;
        submit.textContent = 'Saving…';
        dlg.setAttribute('aria-busy', 'true');
        const data = new FormData();
        data.set('id', String(current.id));
        data.set('date', dateVal);
        data.set('time', timeVal);
        data.set('guests', String(guests));
        data.set('specialRequest', requestBox.value);
        if (typeId !== null) {
            const c = chosenType();
            data.set('typeId', String(typeId));
            data.set('acceptSpend', String(spendBox.checked));
            data.set('expectedSpend', String(c ? c.minimumSpend : 0));
        }
        data.set('__RequestVerificationToken', token.value);
        try {
            const response = await fetch(cfg.changeUrl, {
                method: 'POST', body: data, credentials: 'same-origin',
                headers: { 'Accept': 'application/json', 'X-Requested-With': 'XMLHttpRequest' }
            });
            if (response.redirected) { window.location.assign(response.url); return; }
            if (response.status === 401 || response.status === 403) {
                show('bad', 'Your session has ended or access is unavailable. Refresh the page and sign in again.'); return;
            }
            if (!(response.headers.get('content-type') || '').includes('application/json'))
                throw new Error('Unexpected response');
            const result = await response.json();
            if (!response.ok || !result.success) {
                show('bad', result.message || 'We could not change this reservation. Please refresh and check its latest status.');
                if (['too_late', 'status_changed', 'not_found'].includes(result.code)) current.canCancel = false;
                else check();
                return;
            }
            // Reload so the list, the count and the status come from the server. The success popup
            // is the server's TempData["SuccessMessage"].
            window.location.reload();
        } catch {
            show('bad', 'We could not confirm the result. Refresh the page to check this reservation before trying again.');
        } finally {
            busy = false;
            keep.disabled = false;
            submit.textContent = 'Save changes';
            dlg.removeAttribute('aria-busy');
            submit.disabled = !(verdict && verdict.available && spendOk()) || !canChange(current);
        }
    });

    // Presentation only: the server checks the time and status again.
    const sync = () => items.forEach(item => {
        if (canChange(item)) return;
        page.querySelectorAll('[data-change="' + Number(item.id) + '"]').forEach(b => { b.hidden = true; });
    });
    sync();
    const tick = window.setInterval(() => {
        sync();
        const open = byId('myrOpenChange');
        const item = items.get(viewId);
        if (open && item) open.hidden = !canChange(item);
    }, 15000);
    window.addEventListener('pagehide', () => window.clearInterval(tick));
})();
