// =====================================================================
// YORU pickers (Member 4)
// Themed pop-ups for dropdowns (<select>) and date boxes
// (<input type="date"> / <input type="datetime-local">).
// Used by: Staff/Billing, Customer/Payments, Admin/Payments, Admin/Announcements.
//
// - Mark a control with data-yp. The real <select> / <input> stays where it
//   is, so form posts, validation (min / max) and each page's own JS
//   (change events, form.reset(), .value = ...) work exactly as before.
// - Only the pop-up is replaced: the browser's grey list / calendar becomes
//   one that takes its colours from the page (yoru-pick.css + --yp-* vars
//   set in the page's own CSS).
// - Keyboard: the box keeps its normal keys (type a date, arrows change a
//   select). Alt+Down, F4 or Space opens the pop-up; Esc closes it.
// - Touch screens keep the phone's own picker; Firefox keeps its own
//   calendar for date boxes (it cannot be turned off there).
// =====================================================================
(function () {
    'use strict';

    if (window.matchMedia && window.matchMedia('(pointer: coarse)').matches) return;

    const isFirefox = /firefox/i.test(navigator.userAgent);
    const MONTHS = ['January', 'February', 'March', 'April', 'May', 'June', 'July',
        'August', 'September', 'October', 'November', 'December'];
    const DAYS = ['Su', 'Mo', 'Tu', 'We', 'Th', 'Fr', 'Sa'];
    const pad = n => String(n).padStart(2, '0');
    const isoDate = d => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
    let uid = 0;
    let current = null;   // the open pop-up: { el, pop, close(focusBack) }

    // ---------- shared pop-up handling ----------

    // Inside a <dialog> the pop-up must stay in the dialog (top layer);
    // otherwise it goes in the page root so it gets the page's colours.
    function hostFor(el) {
        return el.closest('dialog') || el.closest('[data-yp-scope]') || document.body;
    }

    function place(pop, anchor) {
        const r = anchor.getBoundingClientRect();
        const vw = document.documentElement.clientWidth;
        const vh = window.innerHeight;
        const w = pop.offsetWidth;
        const h = pop.offsetHeight;
        let left = Math.max(8, Math.min(r.left, vw - w - 8));
        let top = r.bottom + 6;
        if (top + h > vh - 8 && r.top - h - 6 > 8) top = r.top - h - 6;   // no room below: open upwards
        pop.style.left = left + 'px';
        pop.style.top = top + 'px';
    }

    function makePop(el, extraClass) {
        const pop = document.createElement('div');
        pop.className = 'yp-pop ' + extraClass;
        pop.id = 'yp-pop-' + (++uid);
        // Clicking inside the pop-up must not take focus from the box.
        pop.addEventListener('mousedown', e => e.preventDefault());
        hostFor(el).appendChild(pop);
        return pop;
    }

    function track(el, pop, closeFn) {
        const onOutside = e => {
            if (!pop.contains(e.target) && e.target !== el) closeFn(false);
        };
        const onMove = () => place(pop, el);
        document.addEventListener('mousedown', onOutside, true);
        window.addEventListener('scroll', onMove, true);
        window.addEventListener('resize', onMove);
        return () => {
            document.removeEventListener('mousedown', onOutside, true);
            window.removeEventListener('scroll', onMove, true);
            window.removeEventListener('resize', onMove);
        };
    }

    function fire(el) {
        el.dispatchEvent(new Event('input', { bubbles: true }));
        el.dispatchEvent(new Event('change', { bubbles: true }));
    }

    const opensPopup = e => (e.altKey && e.key === 'ArrowDown') || e.key === 'F4' || e.key === ' ';

    // ---------- dropdowns ----------

    function enhanceSelect(sel) {
        let pop = null, items = [], active = -1, untrack = null;

        sel.classList.add('yp-on');
        sel.setAttribute('aria-haspopup', 'listbox');
        sel.setAttribute('aria-expanded', 'false');

        function setActive(i) {
            if (active >= 0 && items[active]) items[active].classList.remove('is-active');
            active = i;
            if (active >= 0 && items[active]) {
                items[active].classList.add('is-active');
                items[active].scrollIntoView({ block: 'nearest' });
            }
        }

        function move(step) {
            if (!items.length) return;
            let i = active;
            for (let n = 0; n < items.length; n++) {
                i = (i + step + items.length) % items.length;
                if (!items[i]._opt.disabled) { setActive(i); return; }
            }
        }

        function choose(opt) {
            if (opt.disabled) return;
            const changed = !opt.selected;
            opt.selected = true;
            close(true);
            if (changed) fire(sel);
        }

        function open() {
            if (sel.disabled || pop) return;
            if (current) current.close(false);

            pop = makePop(sel, 'yp-menu');
            pop.setAttribute('role', 'listbox');
            if (sel.getAttribute('aria-label')) pop.setAttribute('aria-label', sel.getAttribute('aria-label'));
            pop.style.minWidth = sel.offsetWidth + 'px';

            items = Array.from(sel.options).filter(o => !o.hidden).map((o, i) => {
                const row = document.createElement('div');
                row.className = 'yp-opt' + (o.selected ? ' is-on' : '');
                row.id = pop.id + '-' + i;
                row.setAttribute('role', 'option');
                row.setAttribute('aria-selected', o.selected ? 'true' : 'false');
                if (o.disabled) row.setAttribute('aria-disabled', 'true');
                if (o.dataset.ypIcon) row.insertAdjacentHTML('beforeend', `<i class="bi ${o.dataset.ypIcon} yp-opt-icon" aria-hidden="true"></i>`);
                const text = document.createElement('span');
                text.textContent = o.text;
                row.appendChild(text);
                row.insertAdjacentHTML('beforeend', '<i class="bi bi-check2 yp-opt-tick" aria-hidden="true"></i>');
                row._opt = o;
                row.addEventListener('click', () => choose(o));
                row.addEventListener('mousemove', () => { if (active !== i) setActive(i); });
                pop.appendChild(row);
                return row;
            });

            sel.setAttribute('aria-expanded', 'true');
            sel.setAttribute('aria-controls', pop.id);
            place(pop, sel);
            setActive(items.findIndex(r => r._opt.selected));
            untrack = track(sel, pop, close);
            current = { el: sel, pop, close };
        }

        function close(focusBack) {
            if (!pop) return;
            untrack && untrack();
            pop.remove();
            pop = null; items = []; active = -1;
            sel.setAttribute('aria-expanded', 'false');
            sel.removeAttribute('aria-controls');
            if (current && current.el === sel) current = null;
            if (focusBack) sel.focus();
        }

        sel.addEventListener('mousedown', e => {
            if (e.button !== 0) return;
            e.preventDefault();          // no browser list
            sel.focus();
            pop ? close(false) : open();
        });

        sel.addEventListener('keydown', e => {
            if (!pop) {
                if (opensPopup(e) || e.key === 'Enter') { e.preventDefault(); open(); }
                return;
            }
            switch (e.key) {
                case 'ArrowDown': e.preventDefault(); move(1); break;
                case 'ArrowUp': e.preventDefault(); move(-1); break;
                case 'Home': e.preventDefault(); setActive(-1); move(1); break;
                case 'End': e.preventDefault(); setActive(items.length); move(-1); break;
                case 'Enter':
                case ' ':
                    e.preventDefault();
                    if (items[active]) choose(items[active]._opt);
                    break;
                case 'Escape': e.preventDefault(); e.stopPropagation(); close(true); break;
                case 'Tab': close(false); break;
            }
        });

        sel.addEventListener('blur', () => close(false));
    }

    // ---------- date / date-time boxes ----------

    function enhanceDate(input) {
        const withTime = input.type === 'datetime-local';
        let pop = null, untrack = null;
        let view = null;        // first day of the month on show
        let mode = 'days';      // 'days' | 'months'

        input.classList.add('yp-on');

        const parts = () => {
            const v = input.value || '';
            return { date: v.slice(0, 10), time: withTime ? (v.slice(11, 16) || '') : '' };
        };
        const limit = attr => (input.getAttribute(attr) || '').slice(0, 10);
        const allowed = iso => {
            const min = limit('min'), max = limit('max');
            return (!min || iso >= min) && (!max || iso <= max);
        };

        function setValue(date, time) {
            input.value = withTime ? (date ? `${date}T${time || '00:00'}` : '') : date;
            fire(input);
        }

        function head(title, jp) {
            return `<div class="yp-cal-head">
                <button type="button" class="yp-nav" data-yp-nav="-1" aria-label="Previous"><i class="bi bi-chevron-left" aria-hidden="true"></i></button>
                <button type="button" class="yp-cal-title" data-yp-mode>
                    <small class="yp-cal-jp" aria-hidden="true">${jp}</small>
                    <span>${title}</span>
                </button>
                <button type="button" class="yp-nav" data-yp-nav="1" aria-label="Next"><i class="bi bi-chevron-right" aria-hidden="true"></i></button>
            </div>`;
        }

        function render() {
            const { date, time } = parts();
            const today = isoDate(new Date());
            const y = view.getFullYear(), m = view.getMonth();
            let html;

            if (mode === 'months') {
                html = head(String(y), `${y}年`) + '<div class="yp-cal-months">' +
                    MONTHS.map((name, i) => {
                        const on = date && Number(date.slice(0, 4)) === y && Number(date.slice(5, 7)) === i + 1;
                        // a month is open if any of its days is allowed
                        const first = `${y}-${pad(i + 1)}-01`, last = isoDate(new Date(y, i + 1, 0));
                        const min = limit('min'), max = limit('max');
                        const ok = (!max || first <= max) && (!min || last >= min);
                        return `<button type="button" class="yp-month${on ? ' is-on' : ''}" data-yp-month="${i}"${ok ? '' : ' disabled'}>${name.slice(0, 3)}</button>`;
                    }).join('') + '</div>';
            } else {
                const start = new Date(y, m, 1 - new Date(y, m, 1).getDay());
                let grid = '';
                for (let i = 0; i < 42; i++) {
                    const d = new Date(start.getFullYear(), start.getMonth(), start.getDate() + i);
                    const iso = isoDate(d);
                    const cls = ['yp-day'];
                    if (d.getMonth() !== m) cls.push('is-out');
                    if (iso === today) cls.push('is-today');
                    if (iso === date) cls.push('is-on');
                    grid += `<button type="button" class="${cls.join(' ')}" data-yp-day="${iso}"${allowed(iso) ? '' : ' disabled'}` +
                        `${iso === date ? ' aria-pressed="true"' : ''} aria-label="${d.getDate()} ${MONTHS[d.getMonth()]} ${d.getFullYear()}">${d.getDate()}</button>`;
                }
                html = head(`${MONTHS[m]} ${y}`, `${m + 1}月`) +
                    '<div class="yp-cal-main"><div class="yp-cal-days">' +
                    `<div class="yp-cal-week">${DAYS.map(d => `<span>${d}</span>`).join('')}</div>` +
                    `<div class="yp-cal-grid">${grid}</div></div>`;

                if (withTime) {
                    const hh = time.slice(0, 2), mm = time.slice(3, 5);
                    const mins = [];
                    for (let i = 0; i < 60; i += 5) mins.push(pad(i));
                    if (mm && !mins.includes(mm)) mins.push(mm);   // keep a typed minute (e.g. 59)
                    mins.sort();
                    let hours = '';
                    for (let h = 0; h < 24; h++) {
                        hours += `<button type="button" class="yp-tm${pad(h) === hh ? ' is-on' : ''}" data-yp-hour="${pad(h)}">${pad(h)}</button>`;
                    }
                    html += '<div class="yp-time">' +
                        `<div class="yp-col"><span class="yp-col-h">HR</span>${hours}</div>` +
                        `<div class="yp-col"><span class="yp-col-h">MIN</span>${mins.map(x =>
                            `<button type="button" class="yp-tm${x === mm ? ' is-on' : ''}" data-yp-min="${x}">${x}</button>`).join('')}</div>` +
                        '</div>';
                }
                html += '</div>';
            }

            const canToday = allowed(today);
            html += '<div class="yp-cal-foot">' +
                `<button type="button" class="yp-link" data-yp-today${canToday ? '' : ' disabled'}>${withTime ? 'Now' : 'Today'}</button>` +
                (withTime
                    ? '<button type="button" class="yp-done" data-yp-done>Done</button>'
                    : (input.required ? '' : '<button type="button" class="yp-link is-soft" data-yp-clear>Clear</button>')) +
                '</div>';

            pop.innerHTML = html;
            pop.classList.toggle('has-time', withTime && mode === 'days');

            // time columns as tall as the calendar; scroll the chosen hour / minute into view
            if (withTime && mode === 'days') {
                const days = pop.querySelector('.yp-cal-days');
                pop.querySelectorAll('.yp-col').forEach(col => {
                    col.style.height = days.offsetHeight + 'px';
                    const on = col.querySelector('.is-on');
                    if (on) col.scrollTop = on.offsetTop - col.clientHeight / 2 + on.offsetHeight / 2;
                });
            }
            place(pop, input);
        }

        function showMonthOf(iso) {
            const d = iso ? new Date(Number(iso.slice(0, 4)), Number(iso.slice(5, 7)) - 1, 1) : new Date();
            view = new Date(d.getFullYear(), d.getMonth(), 1);
        }

        function onClick(e) {
            const b = e.target.closest('button');
            if (!b || b.disabled) return;
            const { date, time } = parts();

            if (b.hasAttribute('data-yp-nav')) {
                const step = Number(b.dataset.ypNav);
                view = mode === 'months'
                    ? new Date(view.getFullYear() + step, view.getMonth(), 1)
                    : new Date(view.getFullYear(), view.getMonth() + step, 1);
                render();
            } else if (b.hasAttribute('data-yp-mode')) {
                mode = mode === 'days' ? 'months' : 'days';
                render();
            } else if (b.dataset.ypMonth) {
                view = new Date(view.getFullYear(), Number(b.dataset.ypMonth), 1);
                mode = 'days';
                render();
            } else if (b.dataset.ypDay) {
                setValue(b.dataset.ypDay, time);
                if (withTime) { showMonthOf(b.dataset.ypDay); render(); } else close(true);
            } else if (b.dataset.ypHour || b.dataset.ypMin) {
                const day = date || isoDate(new Date());
                const hh = b.dataset.ypHour || time.slice(0, 2) || '00';
                const mm = b.dataset.ypMin || time.slice(3, 5) || '00';
                setValue(day, `${hh}:${mm}`);
                render();
            } else if (b.hasAttribute('data-yp-today')) {
                const now = new Date();
                setValue(isoDate(now), `${pad(now.getHours())}:${pad(now.getMinutes())}`);
                if (withTime) { showMonthOf(isoDate(now)); mode = 'days'; render(); } else close(true);
            } else if (b.hasAttribute('data-yp-clear')) {
                setValue('', '');
                close(true);
            } else if (b.hasAttribute('data-yp-done')) {
                close(true);
            }
        }

        // typing in the box moves the calendar along
        function onType() {
            if (!pop) return;
            const { date } = parts();
            if (date) showMonthOf(date);
            mode = 'days';
            render();
        }

        function open() {
            if (input.disabled || input.readOnly || pop) return;
            if (current) current.close(false);
            mode = 'days';
            showMonthOf(parts().date);
            pop = makePop(input, 'yp-cal');
            pop.setAttribute('role', 'dialog');
            pop.setAttribute('aria-label', withTime ? 'Choose date and time' : 'Choose a date');
            pop.addEventListener('click', onClick);
            input.setAttribute('aria-expanded', 'true');
            render();
            untrack = track(input, pop, close);
            current = { el: input, pop, close };
        }

        function close(focusBack) {
            if (!pop) return;
            untrack && untrack();
            pop.remove();
            pop = null;
            input.setAttribute('aria-expanded', 'false');
            if (current && current.el === input) current = null;
            if (focusBack) input.focus();
        }

        input.setAttribute('aria-haspopup', 'dialog');
        input.setAttribute('aria-expanded', 'false');
        input.addEventListener('click', () => open());
        input.addEventListener('input', onType);
        input.addEventListener('keydown', e => {
            if (opensPopup(e)) { e.preventDefault(); pop ? close(true) : open(); }
            else if (e.key === 'Escape' && pop) { e.preventDefault(); e.stopPropagation(); close(true); }
            else if (e.key === 'Tab') close(false);
        });
        input.addEventListener('blur', () => {
            // let a click inside the pop-up finish first
            setTimeout(() => { if (pop && document.activeElement !== input) close(false); }, 0);
        });
    }

    // ---------- start ----------

    function init(root) {
        root.querySelectorAll('select[data-yp]:not(.yp-on)').forEach(enhanceSelect);
        if (!isFirefox) {
            root.querySelectorAll('input[type="date"][data-yp]:not(.yp-on), input[type="datetime-local"][data-yp]:not(.yp-on)')
                .forEach(enhanceDate);
        }
    }

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', () => init(document));
    else init(document);
})();
