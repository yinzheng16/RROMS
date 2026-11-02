/* Owner: Member 2 - US20 "Service & Seating". Count-up numbers, hover tips, heatmap click-through, filter + table toggles. */
(function () {
    'use strict';
    var root = document.getElementById('occ');
    if (!root) return;

    var reduce = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;

    function esc(s) {
        return String(s).replace(/[&<>"']/g, function (c) {
            return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
        });
    }

    /* ---------- numbers count up when the page opens ---------- */
    function countUp(el) {
        var target = parseFloat(el.getAttribute('data-count'));
        if (isNaN(target)) return;
        var dec = parseInt(el.getAttribute('data-dec') || '0', 10);
        var final = target.toFixed(dec);
        if (reduce || target === 0) { el.textContent = final; return; }
        var start = null, dur = 1100;
        function frame(ts) {
            if (start === null) start = ts;
            var p = Math.min(1, (ts - start) / dur);
            var eased = 1 - Math.pow(1 - p, 3);
            el.textContent = (target * eased).toFixed(dec);
            if (p < 1) requestAnimationFrame(frame); else el.textContent = final;
        }
        el.textContent = (0).toFixed(dec);
        requestAnimationFrame(frame);
    }
    var counters = root.querySelectorAll('[data-count]');
    Array.prototype.forEach.call(counters, function (el, i) {
        setTimeout(function () { countUp(el); }, reduce ? 0 : 450 + i * 70);
    });

    /* ---------- hover / focus tip (first line bold, lines split by |) ---------- */
    var tip = document.getElementById('occTip');
    function showTip(target, x, y) {
        var raw = target.getAttribute('data-tip');
        if (!raw || !tip) return;
        var lines = raw.split('|');
        tip.innerHTML = '<b>' + esc(lines[0]) + '</b>' + lines.slice(1).map(function (l) { return '<span>' + esc(l) + '</span><br>'; }).join('');
        tip.hidden = false;
        place(x, y);
        tip.classList.add('is-on');
    }
    function place(x, y) {
        if (!tip) return;
        var w = tip.offsetWidth, h = tip.offsetHeight;
        var left = Math.min(window.innerWidth - w - 10, Math.max(10, x + 14));
        var top = y - h - 14;
        if (top < 10) top = y + 22;
        tip.style.left = left + 'px';
        tip.style.top = top + 'px';
    }
    function hideTip() {
        if (!tip) return;
        tip.classList.remove('is-on');
        tip.hidden = true;
    }
    root.addEventListener('mouseover', function (e) {
        var t = e.target.closest('[data-tip]');
        if (t) showTip(t, e.clientX, e.clientY);
    });
    root.addEventListener('mousemove', function (e) {
        if (tip && tip.classList.contains('is-on')) place(e.clientX, e.clientY);
    });
    root.addEventListener('mouseout', function (e) {
        if (e.target.closest('[data-tip]')) hideTip();
    });
    root.addEventListener('focusin', function (e) {
        var t = e.target.closest('[data-tip]');
        if (!t) return;
        var r = t.getBoundingClientRect();
        showTip(t, r.left + r.width / 2, r.top);
    });
    root.addEventListener('focusout', hideTip);
    window.addEventListener('scroll', hideTip, { passive: true });

    /* ---------- filter panel ---------- */
    var fbtn = document.getElementById('occFilterBtn');
    var fpanel = document.getElementById('occFilter');
    if (fbtn && fpanel) {
        fbtn.addEventListener('click', function () {
            var open = fpanel.hidden;
            fpanel.hidden = !open;
            fbtn.setAttribute('aria-expanded', open ? 'true' : 'false');
        });
    }

    /* ---------- "view all tables": 10 per page, "Show less" at the bottom ---------- */
    var more = document.getElementById('occMore');
    var all = document.getElementById('occAll');
    if (more && all) {
        var label = more.querySelector('span');
        var total = label ? label.textContent.replace('View all ', '') : '';
        var less = document.getElementById('occLess');
        var pager = document.getElementById('occPager');
        var pInfo = document.getElementById('occPageInfo');
        var pPrev = document.getElementById('occPrev');
        var pNext = document.getElementById('occNext');
        var rows = Array.prototype.slice.call(all.querySelectorAll('tbody tr'));
        var PAGE = 10, page = 0, pages = Math.max(1, Math.ceil(rows.length / PAGE));

        // let the box glide from its old height to its new one
        function glide(el, change, done) {
            if (reduce) { change(); if (done) done(); return; }
            var h0 = el.offsetHeight;
            change();
            var h1 = el.offsetHeight;
            if (h0 === h1) { if (done) done(); return; }
            el.style.overflow = 'hidden';
            el.style.transition = 'none';
            el.style.height = h0 + 'px';
            el.getBoundingClientRect();
            el.style.transition = 'height .42s cubic-bezier(.3,.7,.2,1)';
            el.style.height = h1 + 'px';
            clearTimeout(el._t);
            el._t = setTimeout(function () {
                el.style.height = ''; el.style.overflow = ''; el.style.transition = '';
                if (done) done();
            }, 450);
        }

        var tbl = all.querySelector('table');
        var fullH = 0;   // height of a full page, so the buttons below never move
        function keepHeight() {
            if (!tbl) return;
            tbl.style.marginBottom = '';
            if (page === 0) { fullH = tbl.offsetHeight; return; }
            if (fullH) tbl.style.marginBottom = (14 + Math.max(0, fullH - tbl.offsetHeight)) + 'px';
        }
        function paint() {
            rows.forEach(function (r, i) { r.hidden = Math.floor(i / PAGE) !== page; });
            if (pager) {
                pager.hidden = pages < 2;
                pInfo.textContent = 'Page ' + (page + 1) + ' of ' + pages + ' · ' + (page * PAGE + 1) + '–' + Math.min(rows.length, (page + 1) * PAGE) + ' of ' + rows.length;
                pPrev.disabled = page === 0;
                pNext.disabled = page === pages - 1;
            }
            keepHeight();
        }
        function showPage(n, animate) {
            page = Math.min(Math.max(n, 0), pages - 1);
            if (animate) glide(all, paint); else paint();
        }
        showPage(0);

        function setOpen(open) {
            more.setAttribute('aria-expanded', open ? 'true' : 'false');
            more.style.display = open ? 'none' : '';
            var panel = more.closest('.occ-tables');
            if (open) { page = 0; paint(); }   // measure a full first page again, fonts are ready by now
            glide(all, function () {
                all.classList.toggle('is-open', open);
                if (!open) { page = 0; paint(); }
            }, function () {
                // scroll only after the box has settled, so the two never fight
                if (open) all.scrollIntoView({ behavior: reduce ? 'auto' : 'smooth', block: 'nearest' });
                else if (panel) panel.scrollIntoView({ behavior: reduce ? 'auto' : 'smooth', block: 'nearest' });
            });
        }
        more.addEventListener('click', function () { setOpen(true); });
        if (less) less.addEventListener('click', function () { setOpen(false); });
        if (pPrev) pPrev.addEventListener('click', function () { showPage(page - 1, true); });
        if (pNext) pNext.addEventListener('click', function () { showPage(page + 1, true); });
    }

    /* ---------- click a heatmap box to explore that time slot ---------- */
    var slot = document.getElementById('occSlot');
    var slotUrl = root.getAttribute('data-slot-url');
    var DAYS = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'];
    var token = 0;

    function hour12(h) { return (h % 12 === 0 ? 12 : h % 12) + ' ' + (h < 12 ? 'AM' : 'PM'); }
    function fmt(n, d) { return Number(n).toFixed(d === undefined ? 0 : d).replace(/\.0$/, ''); }

    function render(d) {
        var pct = d.percent === null || d.percent === undefined ? '—' : fmt(d.percent, 1) + '%';
        var counted = d.daysCounted === 1 ? '1 ' + DAYS[d.day] : d.daysCounted + ' ' + DAYS[d.day] + 's';
        var html = '<div class="occ-slot-in">' +
            '<button type="button" class="occ-slot-x" id="occSlotX" aria-label="Close details"><i class="bi bi-x-lg" aria-hidden="true"></i></button>' +
            '<div class="occ-slot-top"><h3>' + esc(DAYS[d.day]) + ' · ' + esc(hour12(d.hour)) + '</h3>' +
            '<span>average over ' + esc(counted) + '</span></div>' +
            '<div class="occ-slot-grid">' +
            '<div><small>Tables in use</small><b>' + esc(pct) + '</b></div>' +
            '<div><small>Reservations</small><b>' + d.reservations + '</b></div>' +
            '<div><small>Walk-ins</small><b>' + d.walkIns + '</b></div>' +
            '<div><small>Avg party</small><b>' + (d.reservations > 0 ? fmt(d.avgGuests, 1) : '—') + '</b></div>' +
            '</div>';
        var more = [];
        if (d.topTables && d.topTables.length) {
            more.push('<span><em>Used most here:</em> ' + d.topTables.map(function (t) {
                return esc(t.name) + ' (' + fmt(t.hours, 1) + ' h)';
            }).join(', ') + '</span>');
        }
        if (d.busiestDate) {
            var parts = [];
            if (d.busiestDateReservations) parts.push(d.busiestDateReservations + (d.busiestDateReservations === 1 ? ' reservation' : ' reservations'));
            if (d.busiestDateWalkIns) parts.push(d.busiestDateWalkIns + (d.busiestDateWalkIns === 1 ? ' walk-in' : ' walk-ins'));
            more.push('<span><em>Busiest day:</em> ' + esc(d.busiestDateText) + ' · ' + parts.join(', ') +
                (d.staffUrl ? ' · <a href="' + esc(d.staffUrl) + '">Open that day</a>' : '') + '</span>');
        }
        if (!d.reservations && !d.walkIns) more.push('<span><em>No parties in this slot for the chosen range.</em></span>');
        html += '<div class="occ-slot-more">' + more.join('') + '</div></div>';
        swap(html);
    }

    // put new content in the box and let its height glide to the new size
    function swap(html) {
        if (reduce) { slot.innerHTML = html; return; }
        var h0 = slot.offsetHeight;
        slot.innerHTML = html;
        var h1 = slot.offsetHeight;
        if (h0 === h1) return;
        slot.style.overflow = 'hidden';
        slot.style.transition = 'none';
        slot.style.height = h0 + 'px';
        slot.getBoundingClientRect();
        slot.style.transition = 'height .34s cubic-bezier(.4,0,.2,1)';
        slot.style.height = h1 + 'px';
        clearTimeout(slot._t);
        slot._t = setTimeout(function () { slot.style.height = ''; slot.style.overflow = ''; slot.style.transition = ''; }, 380);
    }

    var EMPTY = '<p class="occ-slot-empty"><i class="bi bi-hand-index" aria-hidden="true"></i> Select a time slot to explore.</p>';

    // back to the resting hint: click the box again, the x, or press Esc
    function closeSlot() {
        token++;                                   // ignore a reply that is still on its way
        var prev = root.querySelector('.occ-cell.is-sel');
        if (prev) prev.classList.remove('is-sel');
        var inner = slot.firstElementChild;
        if (inner && !reduce) {
            inner.classList.add('is-leaving');
            setTimeout(function () { if (!root.querySelector('.occ-cell.is-sel')) swap(EMPTY); }, 200);
        } else {
            swap(EMPTY);
        }
    }

    function select(btn) {
        if (btn.classList.contains('is-sel')) { closeSlot(); return; }
        var prev = root.querySelector('.occ-cell.is-sel');
        if (prev) prev.classList.remove('is-sel');
        btn.classList.add('is-sel');

        var my = ++token;
        // the placeholder goes in at once; only the real content glides the height
        slot.innerHTML = '<div class="occ-skel" style="width:40%"></div><div class="occ-skel"></div><div class="occ-skel" style="width:70%"></div>';

        var q = new URLSearchParams(window.location.search);
        q.delete('handler');
        q.set('day', btn.getAttribute('data-day'));
        q.set('hour', btn.getAttribute('data-hour'));
        var sep = slotUrl.indexOf('?') >= 0 ? '&' : '?';

        fetch(slotUrl + sep + q.toString(), { headers: { 'Accept': 'application/json' }, credentials: 'same-origin' })
            .then(function (r) { if (!r.ok) throw new Error(r.status); return r.json(); })
            .then(function (d) { if (my === token) render(d); })
            .catch(function () {
                if (my === token) slot.innerHTML = '<p class="occ-slot-empty"><i class="bi bi-exclamation-circle"></i> Could not load that time slot. Please try again.</p>';
            });
    }

    root.addEventListener('click', function (e) {
        if (e.target.closest('#occSlotX')) { closeSlot(); return; }
        var btn = e.target.closest('button.occ-cell');
        if (btn && slot && slotUrl) select(btn);
    });

    /* ---------- quick ranges (7 / 30 / 90 days) and Export CSV use the filter as it is now ----------
       The dining area, table type and count chosen in the filter stay, even before Apply is pressed;
       Export also takes the dates that are showing in the filter. */
    document.addEventListener('click', function (e) {
        var link = e.target.closest('.occ-seg a, a[href*="handler=Export"]');
        var form = document.getElementById('occFilter');
        if (!link || !form) return;

        var url = new URL(link.getAttribute('href'), window.location.href);
        var names = ['area', 'type', 'mode'];
        if (link.href.indexOf('handler=Export') >= 0) names = names.concat(['from', 'to']);
        names.forEach(function (name) {
            var field = form.elements[name];
            if (!field) return;
            if (field.value) url.searchParams.set(name, field.value); else url.searchParams.delete(name);
        });
        link.setAttribute('href', url.pathname + url.search);
    });

    /* ---------- themed date + dropdown pickers inside the filter ---------- */
    var MONTHS = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'];
    var MON3 = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
    var openPop = null;

    function closePop() {
        if (!openPop) return;
        openPop.pop.hidden = true;
        openPop.btn.setAttribute('aria-expanded', 'false');
        openPop = null;
    }
    function togglePop(btn, pop, onOpen) {
        var wasOpen = openPop && openPop.pop === pop;
        closePop();
        if (wasOpen) return;
        if (onOpen) onOpen();
        pop.hidden = false;
        pop.style.left = '0';
        pop.style.right = 'auto';
        if (pop.getBoundingClientRect().right > window.innerWidth - 8) { pop.style.left = 'auto'; pop.style.right = '0'; }
        btn.setAttribute('aria-expanded', 'true');
        openPop = { btn: btn, pop: pop };
    }
    document.addEventListener('click', function (e) {
        // composedPath still lists a clicked button that the calendar has just redrawn (month arrows)
        if (!openPop) return;
        var path = e.composedPath ? e.composedPath() : [];
        if (path.indexOf(openPop.pop) === -1 && path.indexOf(openPop.btn) === -1) closePop();
    });
    document.addEventListener('keydown', function (e) {
        if (e.key === 'Escape' && openPop) { var b = openPop.btn; closePop(); b.focus(); return; }
        if (e.key === 'Escape' && root.querySelector('.occ-cell.is-sel')) closeSlot();
    });

    function pad(n) { return (n < 10 ? '0' : '') + n; }
    function iso(d) { return d.getFullYear() + '-' + pad(d.getMonth() + 1) + '-' + pad(d.getDate()); }
    function parseIso(v) {
        var m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(v || '');
        return m ? new Date(+m[1], +m[2] - 1, +m[3]) : null;
    }
    function nice(v) { var d = parseIso(v); return d ? d.getDate() + ' ' + MON3[d.getMonth()] + ' ' + d.getFullYear() : 'Pick a date'; }

    var errBox = document.getElementById('occDateErr');
    var applyBtn = document.getElementById('occApply');
    var dateInputs = {};

    function validateDates() {
        var f = dateInputs.from, t = dateInputs.to;
        if (!f || !t || !errBox) return;
        var a = parseIso(f.input.value), b = parseIso(t.input.value);
        var bad = !a || !b || a > b;
        f.btn.classList.toggle('is-invalid', bad);
        t.btn.classList.toggle('is-invalid', bad);
        if (applyBtn) applyBtn.disabled = bad;
        var msg = errBox.querySelector('span');
        if (bad) {
            if (msg) msg.textContent = (!a || !b) ? 'Please pick both dates.' : "The To date can't be earlier than the From date.";
            errBox.hidden = false;
        } else {
            errBox.hidden = true;
        }
    }

    function buildDate(input) {
        var field = input.parentNode;
        var max = parseIso(input.getAttribute('max'));
        var label = input.getAttribute('data-label') || 'Date';
        var btn = document.createElement('button');
        btn.type = 'button';
        btn.className = 'occ-pick';
        btn.setAttribute('aria-haspopup', 'dialog');
        btn.setAttribute('aria-expanded', 'false');
        btn.setAttribute('aria-label', label);
        btn.innerHTML = '<i class="bi bi-calendar3" aria-hidden="true"></i><span></span><i class="bi bi-chevron-down occ-chev" aria-hidden="true"></i>';
        var txt = btn.querySelector('span');
        var pop = document.createElement('div');
        pop.className = 'occ-pop occ-cal';
        pop.hidden = true;
        pop.setAttribute('role', 'dialog');
        pop.setAttribute('aria-label', 'Choose ' + label.toLowerCase());
        input.type = 'hidden';
        field.appendChild(btn);
        field.appendChild(pop);
        txt.textContent = nice(input.value);

        var view = parseIso(input.value) || max || new Date();
        view = new Date(view.getFullYear(), view.getMonth(), 1);

        function other() { return dateInputs[input.name === 'from' ? 'to' : 'from']; }

        function draw() {
            var sel = parseIso(input.value), o = other(), oDate = o ? parseIso(o.input.value) : null;
            var lo = null, hi = null;
            if (sel && oDate) { lo = sel < oDate ? sel : oDate; hi = sel < oDate ? oDate : sel; }
            var first = new Date(view.getFullYear(), view.getMonth(), 1);
            var lead = (first.getDay() + 6) % 7;                 // Monday first
            var days = new Date(view.getFullYear(), view.getMonth() + 1, 0).getDate();
            var today = new Date(); today = new Date(today.getFullYear(), today.getMonth(), today.getDate());
            var h = '<div class="occ-cal-head"><button type="button" data-nav="-1" aria-label="Previous month"><i class="bi bi-chevron-left"></i></button>' +
                '<b>' + MONTHS[view.getMonth()] + ' ' + view.getFullYear() + '</b>' +
                '<button type="button" data-nav="1" aria-label="Next month"' +
                (max && new Date(view.getFullYear(), view.getMonth() + 1, 1) > max ? ' disabled' : '') + '><i class="bi bi-chevron-right"></i></button></div>' +
                '<div class="occ-cal-week"><span>Mo</span><span>Tu</span><span>We</span><span>Th</span><span>Fr</span><span>Sa</span><span>Su</span></div><div class="occ-cal-days">';
            for (var i = 0; i < lead; i++) h += '<span class="occ-cal-day is-gap"></span>';
            for (var d = 1; d <= days; d++) {
                var cur = new Date(view.getFullYear(), view.getMonth(), d);
                var cls = 'occ-cal-day';
                if (+cur === +today) cls += ' is-today';
                if (lo && cur >= lo && cur <= hi) cls += ' is-range';
                if (sel && +cur === +sel) cls += ' is-sel';
                h += '<button type="button" class="' + cls + '" data-date="' + iso(cur) + '"' + (max && cur > max ? ' disabled' : '') + '>' + d + '</button>';
            }
            h += '</div><div class="occ-cal-foot"><button type="button" data-today>Today</button></div>';
            pop.innerHTML = h;
        }

        function set(v) {
            input.value = v;
            txt.textContent = nice(v);
            closePop();
            validateDates();
        }

        btn.addEventListener('click', function () {
            togglePop(btn, pop, function () {
                var s = parseIso(input.value);
                if (s) view = new Date(s.getFullYear(), s.getMonth(), 1);
                draw();
            });
        });
        pop.addEventListener('click', function (e) {
            var nav = e.target.closest('[data-nav]');
            if (nav) { view = new Date(view.getFullYear(), view.getMonth() + (+nav.getAttribute('data-nav')), 1); draw(); return; }
            var day = e.target.closest('[data-date]');
            if (day) { set(day.getAttribute('data-date')); btn.focus(); return; }
            if (e.target.closest('[data-today]')) set(iso(max && max < new Date() ? max : new Date()));
        });

        dateInputs[input.name] = { input: input, btn: btn };
    }

    var selects = {};

    function buildSelect(sel) {
        var field = sel.parentNode;
        var label = sel.getAttribute('data-label') || 'Choose';
        var btn = document.createElement('button');
        btn.type = 'button';
        btn.className = 'occ-pick';
        btn.setAttribute('aria-haspopup', 'listbox');
        btn.setAttribute('aria-expanded', 'false');
        btn.setAttribute('aria-label', label);
        btn.innerHTML = '<span></span><i class="bi bi-chevron-down occ-chev" aria-hidden="true"></i>';
        var txt = btn.querySelector('span');
        var pop = document.createElement('div');
        pop.className = 'occ-pop';
        pop.hidden = true;
        var ul = document.createElement('ul');
        ul.className = 'occ-list';
        ul.setAttribute('role', 'listbox');
        pop.appendChild(ul);
        sel.hidden = true;
        field.appendChild(btn);
        field.appendChild(pop);

        // every choice the server rendered, so the list can be narrowed and widened again
        var all = Array.prototype.map.call(sel.options, function (o) { return { value: o.value, text: o.text }; });

        function sync() {
            txt.textContent = sel.options[sel.selectedIndex] ? sel.options[sel.selectedIndex].text : '';
            Array.prototype.forEach.call(ul.children, function (li, i) {
                li.setAttribute('aria-selected', i === sel.selectedIndex ? 'true' : 'false');
            });
        }
        function render() {
            ul.innerHTML = '';
            Array.prototype.forEach.call(sel.options, function (o, i) {
                var li = document.createElement('li');
                li.setAttribute('role', 'option');
                li.textContent = o.text;
                li.addEventListener('click', function () {
                    sel.selectedIndex = i;
                    sync();
                    closePop();
                    btn.focus();
                    sel.dispatchEvent(new Event('change'));
                });
                ul.appendChild(li);
            });
            sync();
        }
        render();

        // values = the option values to offer; locked = only one choice, shown but not changeable (and not sent)
        function setOptions(values, keep, locked) {
            sel.innerHTML = '';
            all.forEach(function (c) {
                if (values.indexOf(c.value) === -1) return;
                var o = document.createElement('option');
                o.value = c.value;
                o.text = c.text;
                sel.appendChild(o);
            });
            var idx = 0;
            for (var i = 0; i < sel.options.length; i++) if (sel.options[i].value === keep) idx = i;
            sel.selectedIndex = idx;
            sel.disabled = !!locked;
            btn.disabled = !!locked;
            render();
        }

        var focusIdx = 0;
        function focusItem(i) {
            focusIdx = Math.max(0, Math.min(ul.children.length - 1, i));
            Array.prototype.forEach.call(ul.children, function (li, k) { li.classList.toggle('is-focus', k === focusIdx); });
            ul.children[focusIdx].scrollIntoView({ block: 'nearest' });
        }
        btn.addEventListener('click', function () {
            togglePop(btn, pop, function () { focusItem(sel.selectedIndex); });
        });
        btn.addEventListener('keydown', function (e) {
            if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
                e.preventDefault();
                if (pop.hidden) { togglePop(btn, pop, function () { focusItem(sel.selectedIndex); }); }
                else focusItem(focusIdx + (e.key === 'ArrowDown' ? 1 : -1));
            } else if (e.key === 'Enter' && !pop.hidden) {
                e.preventDefault();
                ul.children[focusIdx].click();
            }
        });

        selects[sel.name] = { sel: sel, all: all, setOptions: setOptions };
    }

    // The table type list follows the dining area: only types that really have a table there.
    function linkAreaAndType() {
        var area = selects.area, type = selects.type;
        if (!area || !type) return;
        var map = {};
        try { map = JSON.parse(fpanel.getAttribute('data-area-types') || '{}'); } catch (e) { map = {}; }
        var everything = type.all.map(function (c) { return c.value; });

        function refresh(keepValue) {
            var a = area.sel.value;
            if (!a) { type.setOptions(everything, keepValue, false); return; }          // any area: every type
            var ids = (map[a] || []).map(String);
            if (ids.length === 0) { type.setOptions([''], '', false); return; }          // untyped tables only
            if (ids.length === 1) { type.setOptions(ids, ids[0], true); return; }        // one type: shown, locked
            type.setOptions([''].concat(ids), keepValue, false);                         // several: "All" + those types
        }
        // a type that was only shown because it was the area's single type is not the admin's own choice: drop it
        area.sel.addEventListener('change', function () { refresh(type.sel.disabled ? '' : type.sel.value); });
        refresh(type.sel.value);
    }

    if (fpanel) {
        Array.prototype.forEach.call(fpanel.querySelectorAll('input[type="date"]'), buildDate);
        Array.prototype.forEach.call(fpanel.querySelectorAll('select'), buildSelect);
        linkAreaAndType();
        validateDates();
        if (errBox && !errBox.hidden && errBox.querySelector('span').textContent === '') errBox.hidden = true;
        if (!fpanel.hidden && fbtn) fbtn.setAttribute('aria-expanded', 'true');
    }
})();
