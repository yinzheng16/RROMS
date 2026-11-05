/* Owner: Member 3 (US29). Admin promotions page.
   - Tabs, sort, calendar, paging and live search update the list in place (no page reload).
   - New / Edit / Run again open the pop-up in place, so the page does not jump to the top.
   - YORU-styled date picker, live preview, confirm dialogs.
   Without JavaScript every link and form still works as a normal page load. */
(() => {
    'use strict';
    const root = document.querySelector('[data-promo-studio]');
    if (!root) return;
    const dialog = root.querySelector('[data-promo-dialog]');
    const searchInput = root.querySelector('[data-promo-search]');
    const searchClear = root.querySelector('[data-promo-search-clear]');
    const formKeys = ['Edit', 'New', 'Copy'];
    const filterKeys = ['Status', 'Search', 'Sort', 'Day', 'Month', 'PageNo'];
    const SCROLL_KEY = 'yoru-promo-scroll';

    // Keep the scroll position across a save / stop / delete (those reload the page).
    try {
        const saved = sessionStorage.getItem(SCROLL_KEY);
        if (saved !== null) { sessionStorage.removeItem(SCROLL_KEY); scrollTo(0, Number(saved) || 0); }
    } catch { /* storage unavailable: start at the top */ }
    root.addEventListener('submit', () => { try { sessionStorage.setItem(SCROLL_KEY, String(scrollY)); } catch { } }, true);

    const listUrl = (from = location.href) => {
        const url = new URL(from, location.href);
        formKeys.forEach(key => url.searchParams.delete(key));
        return url.pathname + url.search;
    };
    const fetchPage = async (url, signal) => {
        const response = await fetch(url, { signal, credentials: 'same-origin', headers: { 'X-Requested-With': 'XMLHttpRequest' } });
        if (!response.ok || response.redirected) throw new Error('reload');
        return new DOMParser().parseFromString(await response.text(), 'text/html');
    };

    // ----- In-place list updates -----
    // Photos that are already on screen are moved into the new list instead of being reloaded,
    // so nothing blinks; the list only dims if the server takes a moment.
    function swapPart(part, fresh) {
        const photos = new Map();
        part.querySelectorAll('img').forEach(img => {
            const key = img.getAttribute('src');
            if (!photos.has(key)) photos.set(key, []);
            photos.get(key).push(img);
        });
        const incoming = document.importNode(fresh, true);
        incoming.querySelectorAll('img').forEach(img => {
            const kept = photos.get(img.getAttribute('src'))?.shift();
            if (kept) img.replaceWith(kept);
        });
        part.replaceWith(incoming);
    }
    let request = null, searchTimer = null;
    async function navigate(href, { push = true, replace = false } = {}) {
        const url = new URL(href, location.href);
        formKeys.forEach(key => url.searchParams.delete(key));
        request?.abort();
        const current = new AbortController(); request = current;
        const dimTimer = setTimeout(() => root.querySelector('[data-promo-swap="list"]')?.classList.add('is-loading'), 250);
        root.setAttribute('aria-busy', 'true');
        try {
            const next = await fetchPage(url, current.signal);
            if (request !== current) return;
            root.querySelectorAll('[data-promo-swap]').forEach(part => {
                const fresh = next.querySelector(`[data-promo-swap="${part.dataset.promoSwap}"]`);
                if (fresh) swapPart(part, fresh);
            });
            refreshNewButtons();
            root.querySelectorAll('form').forEach(f => filterKeys.forEach(name => {
                const field = f.querySelector(`input[type=hidden][name="${name}"]`);
                if (field) field.value = url.searchParams.get(name) ?? '';
            }));
            if (replace) history.replaceState(null, '', url); else if (push) history.pushState(null, '', url);
        } catch (error) {
            if (error.name !== 'AbortError') location.href = url;   // fall back to a normal page load
        } finally {
            clearTimeout(dimTimer);
            if (request === current) {
                request = null;
                root.removeAttribute('aria-busy');
                root.querySelector('[data-promo-swap="list"]')?.classList.remove('is-loading');
            }
        }
    }
    root.addEventListener('click', event => {
        const link = event.target.closest('a[data-promo-nav]');
        if (!link || event.ctrlKey || event.metaKey || event.shiftKey || event.button !== 0) return;
        event.preventDefault();
        link.closest('details')?.removeAttribute('open');
        if (link.matches('[data-promo-reset]') && searchInput) { searchInput.value = ''; if (searchClear) searchClear.hidden = true; }
        const toTop = link.matches('[data-promo-top]');
        navigate(link.href).then(() => {
            if (toTop) root.querySelector('.promo-specials')?.scrollIntoView({ behavior: 'smooth', block: 'start' });
        });
    });
    addEventListener('popstate', () => {
        const url = new URL(location.href);
        if (formKeys.some(key => url.searchParams.has(key))) return;
        if (dialog?.open) dialog.close();
        navigate(location.href, { push: false });
    });

    // Live search (results update while typing, after a short pause).
    function searchNow(push = false) {
        clearTimeout(searchTimer);
        const url = new URL(location.href);
        const term = searchInput.value.trim();
        if (term) url.searchParams.set('Search', term); else url.searchParams.delete('Search');
        url.searchParams.delete('PageNo');
        if (searchClear) searchClear.hidden = !term;
        navigate(url, { push, replace: !push });
    }
    searchInput?.addEventListener('input', () => { clearTimeout(searchTimer); searchTimer = setTimeout(() => searchNow(false), 300); });
    root.querySelector('[data-promo-tools]')?.addEventListener('submit', event => {
        if (!searchInput) return;
        event.preventDefault(); searchNow(true);
    });
    searchClear?.addEventListener('click', event => {
        event.preventDefault();
        searchInput.value = ''; searchNow(true); searchInput.focus();
    });

    // Sort picker: close when clicking elsewhere or pressing Escape.
    document.addEventListener('click', event => {
        root.querySelectorAll('details[data-promo-sort][open]').forEach(d => { if (!d.contains(event.target)) d.removeAttribute('open'); });
    });
    root.addEventListener('keydown', event => {
        const open = event.target.closest?.('details[data-promo-sort][open]');
        if (event.key === 'Escape' && open) { open.removeAttribute('open'); open.querySelector('summary').focus(); }
    });

    // ----- Pop-up form (elements are looked up each time because the content is replaced) -----
    const q = selector => dialog?.querySelector(selector);
    const money = value => 'RM ' + (Math.round(value * 100) / 100).toFixed(2);
    const months = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
    const monthNames = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'];
    const parseDate = value => { const [y, m, d] = (value || '').split('-').map(Number); return y && m && d ? { y, m, d } : null; };
    const iso = (y, m, d) => `${y}-${String(m).padStart(2, '0')}-${String(d).padStart(2, '0')}`;
    const kind = () => q('[data-promo-kind]:checked')?.value ?? 'Percent';

    function dates() {
        const a = parseDate(q('[data-promo-start]')?.value), b = parseDate(q('[data-promo-end]')?.value);
        if (!a || !b) return '';
        if (a.y === b.y && a.m === b.m && a.d === b.d) return `${a.d} ${months[a.m - 1]} ${a.y}`;
        if (a.y === b.y && a.m === b.m) return `${a.d} – ${b.d} ${months[b.m - 1]} ${b.y}`;
        if (a.y === b.y) return `${a.d} ${months[a.m - 1]} – ${b.d} ${months[b.m - 1]} ${b.y}`;
        return `${a.d} ${months[a.m - 1]} ${a.y} – ${b.d} ${months[b.m - 1]} ${b.y}`;
    }
    function updateKind() {
        const percentField = q('[data-promo-percent-field]'), priceField = q('[data-promo-price-field]');
        if (!percentField || !priceField) return;
        const fixed = kind() === 'FixedPrice';
        percentField.hidden = fixed; priceField.hidden = !fixed;
        q('[data-promo-percent]').required = !fixed; q('[data-promo-price]').required = fixed;
        const base = Number(q('[data-promo-dish]')?.selectedOptions[0]?.dataset.price);
        q('[data-promo-price]').max = base > 0 ? (base - 0.01).toFixed(2) : '';
    }
    // Hints show only when a value is wrong (typed and invalid), not while the field is empty.
    function updateHints() {
        const base = Number(q('[data-promo-dish]')?.selectedOptions[0]?.dataset.price);
        const percent = q('[data-promo-percent]'), price = q('[data-promo-price]');
        const start = q('[data-promo-start]'), end = q('[data-promo-end]');
        const show = (hint, field, bad) => {
            const el = q(`[data-hint-${hint}]`);
            if (!el) return;
            if (bad) { el.hidden = false; field?.setAttribute('aria-invalid', 'true'); }
            else if (!el.dataset.server || field?.dataset.touched) { el.hidden = true; field?.removeAttribute('aria-invalid'); }
        };
        // Title and dish: an error shown on Create disappears as soon as the field is filled in.
        const title = q('[data-promo-title]'), dish = q('[data-promo-dish]');
        if (title?.value.trim()) { q('[data-hint-title]')?.setAttribute('hidden', ''); title.removeAttribute('aria-invalid'); }
        if (dish?.value) {
            q('[data-hint-dish]')?.setAttribute('hidden', ''); dish.removeAttribute('aria-invalid');
            dialog.querySelector('.promo-combo__input')?.removeAttribute('aria-invalid');
        }
        const fixed = kind() === 'FixedPrice';
        const pv = percent?.value.trim() ?? '', rv = price?.value.trim() ?? '';
        show('percent', percent, !fixed && pv !== '' && !(Number(pv) >= 1 && Number(pv) <= 99));
        if (fixed && rv !== '' && base > 0 && !(Number(rv) > 0 && Number(rv) < base)) {
            q('[data-hint-price]').textContent = `The promotional price must be lower than the normal price (RM ${base.toFixed(2)}).`;
            show('price', price, true);
        } else show('price', price, false);
        if (fixed) show('percent', percent, false); else show('price', price, false);
        show('dates', end, !!(start?.value && end?.value && end.value < start.value));
        dialog?.querySelectorAll('.promo-date__button').forEach(b => {
            const input = b.previousElementSibling;
            if (input?.getAttribute('aria-invalid') === 'true') b.setAttribute('aria-invalid', 'true'); else b.removeAttribute('aria-invalid');
        });
    }
    function updatePreview() {
        const preview = q('[data-promo-preview]');
        if (!preview) return;
        const p = name => preview.querySelector(`[data-preview-${name}]`);
        const option = q('[data-promo-dish]')?.selectedOptions[0];
        const base = Number(option?.dataset.price);
        const image = option?.dataset.image;
        p('image').hidden = !image; p('placeholder').hidden = !!image;
        if (image && p('image').getAttribute('src') !== image) p('image').src = image;
        p('title').textContent = q('[data-promo-title]')?.value.trim() || 'Your promotion';
        p('dish').textContent = option?.dataset.name || 'Choose a dish';
        p('meta').textContent = option?.dataset.code ? `${option.dataset.code} · ${option.dataset.category}` : '';
        p('dates').textContent = dates();
        preview.classList.toggle('is-empty', !option?.value);
        let now = NaN;
        if (kind() === 'FixedPrice') now = Number(q('[data-promo-price]').value);
        else if (q('[data-promo-percent]').value !== '') now = base * (100 - Number(q('[data-promo-percent]').value)) / 100;
        const valid = base > 0 && now > 0 && now < base;
        p('now').textContent = valid ? money(now) : base > 0 ? money(base) : 'RM –';
        p('was').hidden = !valid; p('off').hidden = !valid;
        p('save').textContent = valid ? '' : '\u00a0';   // the line keeps its space so the card never changes size
        if (valid) {
            p('was').textContent = money(base);
            p('off').textContent = (Math.round((base - now) / base * 1000) / 10) + '% OFF';
            p('save').textContent = `Save ${money(base - now)} per portion`;
        }
    }

    // ----- YORU date picker (replaces the browser's own calendar) -----
    let openPicker = null;
    function closePicker() { openPicker?.remove(); openPicker = null; }
    function label(value) { const d = parseDate(value); return d ? `${d.d} ${months[d.m - 1]} ${d.y}` : 'Choose a date'; }
    function enhanceDates() {
        dialog?.querySelectorAll('input[type=date][data-promo-start], input[type=date][data-promo-end]').forEach(input => {
            if (input.dataset.enhanced) return;
            input.dataset.enhanced = 'true';
            input.classList.add('promo-date__native');
            input.tabIndex = -1; input.setAttribute('aria-hidden', 'true');
            const button = document.createElement('button');
            button.type = 'button'; button.className = 'form-control promo-date__button';
            button.setAttribute('aria-haspopup', 'dialog');
            button.innerHTML = '<span></span><i class="bi bi-calendar3" aria-hidden="true"></i>';
            const field = input.closest('.promo-field');
            const name = field?.querySelector('label')?.textContent?.trim() || 'Date';
            const sync = () => { button.querySelector('span').textContent = label(input.value); button.setAttribute('aria-label', `${name}: ${label(input.value)}`); };
            if (input.getAttribute('aria-invalid') === 'true') button.setAttribute('aria-invalid', 'true');
            input.after(button); sync();
            input.addEventListener('change', sync);
            button.addEventListener('click', () => openPicker?.dataset.for === input.id ? closePicker() : showPicker(input, button));
            field?.querySelector('label')?.setAttribute('for', '');
        });
    }
    function showPicker(input, button) {
        closePicker();
        const picked = parseDate(input.value);
        const min = parseDate(input.min);
        const today = new Date();
        let y = picked?.y ?? today.getFullYear(), m = picked?.m ?? today.getMonth() + 1;
        const pop = document.createElement('div');
        pop.className = 'promo-datepop'; pop.dataset.for = input.id;
        pop.setAttribute('role', 'dialog'); pop.setAttribute('aria-label', 'Choose a date');
        const draw = () => {
            const first = new Date(y, m - 1, 1);
            const offset = (first.getDay() + 6) % 7;
            const days = new Date(y, m, 0).getDate();
            const cells = Math.ceil((offset + days) / 7) * 7;
            const todayIso = iso(today.getFullYear(), today.getMonth() + 1, today.getDate());
            const minIso = min ? iso(min.y, min.m, min.d) : '';
            let grid = '';
            for (let i = 0; i < cells; i++) {
                const d = new Date(y, m - 1, 1 - offset + i);
                const value = iso(d.getFullYear(), d.getMonth() + 1, d.getDate());
                const cls = ['promo-datepop__day'];
                if (d.getMonth() !== m - 1) cls.push('is-outside');
                if (value === todayIso) cls.push('is-today');
                if (value === input.value) cls.push('is-selected');
                const disabled = minIso && value < minIso;
                grid += `<button type="button" class="${cls.join(' ')}" data-value="${value}" ${disabled ? 'disabled' : ''} aria-label="${d.getDate()} ${monthNames[d.getMonth()]} ${d.getFullYear()}">${d.getDate()}</button>`;
            }
            pop.innerHTML = `<div class="promo-datepop__head"><button type="button" data-step="-1" aria-label="Previous month"><i class="bi bi-chevron-left"></i></button>`
                + `<strong>${monthNames[m - 1]} ${y}</strong><button type="button" data-step="1" aria-label="Next month"><i class="bi bi-chevron-right"></i></button></div>`
                + `<div class="promo-datepop__names">${['Mo', 'Tu', 'We', 'Th', 'Fr', 'Sa', 'Su'].map(n => `<span>${n}</span>`).join('')}</div>`
                + `<div class="promo-datepop__grid">${grid}</div>`
                + `<div class="promo-datepop__foot"><button type="button" data-today>Today</button></div>`;
        };
        draw();
        pop.addEventListener('click', event => {
            event.stopPropagation();
            const step = event.target.closest('[data-step]');
            if (step) { m += Number(step.dataset.step); if (m < 1) { m = 12; y--; } if (m > 12) { m = 1; y++; } draw(); return; }
            const day = event.target.closest('[data-value]:not([disabled])');
            const todayBtn = event.target.closest('[data-today]');
            const value = day?.dataset.value ?? (todayBtn ? iso(today.getFullYear(), today.getMonth() + 1, today.getDate()) : null);
            if (!value || (input.min && value < input.min)) return;
            input.value = value;
            input.dispatchEvent(new Event('change', { bubbles: true }));
            closePicker(); button.focus();
        });
        pop.addEventListener('keydown', event => { if (event.key === 'Escape') { event.preventDefault(); event.stopPropagation(); closePicker(); button.focus(); } });
        button.after(pop); openPicker = pop;
        (pop.querySelector('.is-selected') || pop.querySelector('.promo-datepop__day:not([disabled])'))?.focus();
    }
    document.addEventListener('click', event => {
        if (openPicker && !openPicker.contains(event.target) && !event.target.closest('.promo-date__button')) closePicker();
    });

    // ----- Searchable dish picker (the real <select> stays for the form and without JavaScript) -----
    function enhanceDish() {
        const select = q('[data-promo-dish]');
        if (!select || select.dataset.enhanced) return;
        select.dataset.enhanced = 'true';
        select.classList.add('promo-combo__native'); select.tabIndex = -1; select.setAttribute('aria-hidden', 'true');
        const combo = document.createElement('div');
        combo.className = 'promo-combo';
        combo.innerHTML = '<div class="promo-combo__box"><span class="promo-combo__thumb" aria-hidden="true"><i class="bi bi-image"></i></span>'
            + '<input type="text" class="promo-combo__input" role="combobox" aria-expanded="false" aria-autocomplete="list" autocomplete="off" placeholder="Search a dish by name, code or category…" />'
            + '<i class="bi bi-chevron-down promo-combo__chev" aria-hidden="true"></i></div>'
            + '<ul class="promo-combo__list" role="listbox" hidden></ul>';
        select.after(combo);
        const box = combo.querySelector('.promo-combo__box');
        const input = combo.querySelector('input'), list = combo.querySelector('ul'), thumb = combo.querySelector('.promo-combo__thumb');
        const listId = select.id + '-list'; list.id = listId; input.setAttribute('aria-controls', listId);
        input.id = select.id + '-search';
        select.closest('.promo-field')?.querySelector('label')?.setAttribute('for', input.id);
        if (select.getAttribute('aria-invalid') === 'true') input.setAttribute('aria-invalid', 'true');
        const options = [...select.querySelectorAll('option[value]:not([value=""])')];
        let active = -1, shown = [];
        const esc = v => String(v ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
        const describe = o => `${o.dataset.name} · ${o.dataset.code}`;
        const showChosen = () => {
            const o = select.selectedOptions[0];
            input.value = o?.value ? describe(o) : '';
            thumb.innerHTML = o?.dataset.image ? `<img src="${esc(o.dataset.image)}" alt="">` : '<i class="bi bi-image"></i>';
        };
        const render = term => {
            const t = term.trim().toLowerCase();
            shown = options.filter(o => !t || [o.dataset.name, o.dataset.code, o.dataset.category].some(v => v?.toLowerCase().includes(t)));
            let html = '', group = null;
            shown.forEach((o, i) => {
                if (o.dataset.category !== group) { group = o.dataset.category; html += `<li class="promo-combo__group" role="presentation">${esc(group)}</li>`; }
                const unavailable = o.textContent.includes('(unavailable)');
                html += `<li role="option" id="${listId}-${i}" data-index="${i}" class="promo-combo__option${o.selected ? ' is-chosen' : ''}" aria-selected="${o.selected}">`
                    + (o.dataset.image ? `<img src="${esc(o.dataset.image)}" alt="">` : '<span class="promo-combo__noimg"><i class="bi bi-image"></i></span>')
                    + `<span class="promo-combo__text"><strong></strong><small>${esc(o.dataset.code)}${unavailable ? ' · unavailable' : ''}</small></span>`
                    + `<span class="promo-combo__price">RM ${esc(o.dataset.price)}</span></li>`;
            });
            list.innerHTML = html || '<li class="promo-combo__empty">No dish matches.</li>';
            list.querySelectorAll('[data-index] strong').forEach(el => { el.textContent = shown[Number(el.closest('[data-index]').dataset.index)].dataset.name; });
            active = -1;
        };
        const open = () => { render(input.dataset.typed === 'true' ? input.value : ''); list.hidden = false; input.setAttribute('aria-expanded', 'true'); combo.classList.add('is-open'); };
        const close = () => { list.hidden = true; input.setAttribute('aria-expanded', 'false'); combo.classList.remove('is-open'); input.dataset.typed = ''; showChosen(); };
        const choose = i => {
            const o = shown[i]; if (!o) return;
            select.value = o.value; select.dispatchEvent(new Event('change', { bubbles: true }));
            input.removeAttribute('aria-invalid'); close();
        };
        const highlight = i => {
            const items = [...list.querySelectorAll('[data-index]')];
            if (!items.length) return;
            active = (i + items.length) % items.length;
            items.forEach((el, n) => el.classList.toggle('is-active', n === active));
            input.setAttribute('aria-activedescendant', items[active].id);
            // Scroll only the list (scrollIntoView would also scroll the pop-up and shift it).
            const item = items[active];
            if (item.offsetTop < list.scrollTop) list.scrollTop = item.offsetTop - 4;
            else if (item.offsetTop + item.offsetHeight > list.scrollTop + list.clientHeight)
                list.scrollTop = item.offsetTop + item.offsetHeight - list.clientHeight + 4;
        };
        input.addEventListener('focus', () => {
            if (input.dataset.quietFocus) { delete input.dataset.quietFocus; return; }   // focused to show an error: keep the list closed
            input.select(); open();
        });
        select.addEventListener('change', showChosen);   // e.g. a restored draft
        input.addEventListener('click', open);
        box.addEventListener('click', event => { if (event.target !== input) input.focus(); });
        input.addEventListener('input', () => { input.dataset.typed = 'true'; render(input.value); list.hidden = false; combo.classList.add('is-open'); if (shown.length) highlight(0); });
        input.addEventListener('keydown', event => {
            if (event.key === 'ArrowDown') { event.preventDefault(); if (list.hidden) open(); highlight(active + 1); }
            else if (event.key === 'ArrowUp') { event.preventDefault(); highlight(active - 1); }
            else if (event.key === 'Enter') { event.preventDefault(); if (!list.hidden && active >= 0) choose(active); }
            else if (event.key === 'Escape' && !list.hidden) { event.preventDefault(); event.stopPropagation(); close(); }
            else if (event.key === 'Tab') close();
        });
        list.addEventListener('mousedown', event => event.preventDefault());
        list.addEventListener('click', event => { const li = event.target.closest('[data-index]'); if (li) choose(Number(li.dataset.index)); });
        input.addEventListener('blur', () => setTimeout(() => { if (!combo.contains(document.activeElement)) close(); }, 120));
        showChosen();
    }

    // ----- Drafts: closing a form with changes offers Save draft / Discard -----
    const DRAFT_PREFIX = 'yoru-promo-draft:';
    const draftFields = ['Input.Title', 'Input.MenuItemId', 'Input.Kind', 'Input.DiscountPercent', 'Input.PromoPrice', 'Input.StartDate', 'Input.EndDate'];
    let snapshot = '';
    function draftKey() {
        const id = q('input[name="Input.Id"]')?.value || '0';
        const copy = new URL(location.href).searchParams.get('Copy');
        return DRAFT_PREFIX + (id !== '0' ? `edit-${id}` : copy ? `copy-${copy}` : 'new');
    }
    function readForm() {
        const form = q('[data-promo-form]'); if (!form) return {};
        const values = {};
        draftFields.forEach(name => {
            const field = form.querySelector(`[name="${name}"]:checked`) || form.querySelector(`[name="${name}"]:not([type=radio])`);
            if (field) values[name] = field.value;
        });
        return values;
    }
    function writeForm(values) {
        const form = q('[data-promo-form]'); if (!form) return;
        Object.entries(values).forEach(([name, value]) => {
            const radios = form.querySelectorAll(`input[type=radio][name="${name}"]`);
            if (radios.length) radios.forEach(r => { r.checked = r.value === value; });
            else { const field = form.querySelector(`[name="${name}"]`); if (field) field.value = value; }
        });
    }
    const storage = {
        get: key => { try { return JSON.parse(localStorage.getItem(key) || 'null'); } catch { return null; } },
        set: (key, value) => { try { localStorage.setItem(key, JSON.stringify(value)); return true; } catch { return false; } },
        remove: key => { try { localStorage.removeItem(key); } catch { } }
    };
    function restoreDraft() {
        q('[data-promo-draft-note]')?.remove();
        const hasServerError = !!q('.promo-form__error:not([hidden])');
        const draft = hasServerError ? null : storage.get(draftKey());
        if (!draft?.values) return;
        writeForm(draft.values);
        const note = document.createElement('p');
        note.className = 'promo-draft-note'; note.dataset.promoDraftNote = ''; note.setAttribute('role', 'status');
        const when = new Date(draft.savedAt);
        note.innerHTML = '<i class="bi bi-journal-check" aria-hidden="true"></i><span></span><button type="button" data-promo-draft-discard>Discard draft</button>';
        note.querySelector('span').textContent = `Draft restored (saved ${when.toLocaleDateString('en-GB', { day: 'numeric', month: 'short' })}, ${when.toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit' })}).`;
        q('.promo-dialog__fields')?.prepend(note);
    }
    dialog?.addEventListener('click', event => {
        if (!event.target.closest('[data-promo-draft-discard]')) return;
        storage.remove(draftKey());
        refreshNewButtons();
        showToast('Draft discarded.');
        // Reopen the same form without the draft (saved values for Edit, blank for New).
        const href = location.href;
        fetchPage(href).then(next => {
            const fresh = next.querySelector('[data-promo-dialog]');
            if (!fresh) throw new Error('reload');
            dialog.innerHTML = fresh.innerHTML;
            prepareForm();
        }).catch(() => { location.href = href; });
    });
    // Check the form before sending, and send it without reloading the page, so nothing flashes.
    function checkForm() {
        const problems = [];
        const flag = (hint, field, bad) => {
            const el = q(`[data-hint-${hint}]`);
            if (!el) return;
            el.hidden = !bad;
            if (bad) { field?.setAttribute('aria-invalid', 'true'); problems.push(field); } else field?.removeAttribute('aria-invalid');
        };
        const title = q('[data-promo-title]'), dish = q('[data-promo-dish]');
        flag('title', title, !title?.value.trim());
        flag('dish', dish, !dish?.value);
        dialog.querySelector('.promo-combo__input')?.toggleAttribute('aria-invalid', !dish?.value);
        const fixed = kind() === 'FixedPrice';
        const base = Number(dish?.selectedOptions[0]?.dataset.price);
        const percent = q('[data-promo-percent]'), price = q('[data-promo-price]');
        if (fixed) {
            const v = Number(price.value);
            const bad = !price.value || !(v > 0) || (base > 0 && v >= base);
            if (bad && base > 0) q('[data-hint-price]').textContent = `The promotional price must be lower than the normal price (RM ${base.toFixed(2)}).`;
            if (bad && !price.value) q('[data-hint-price]').textContent = 'Enter the promotional price.';
            flag('price', price, bad); flag('percent', percent, false);
        } else {
            const v = Number(percent.value);
            flag('percent', percent, !percent.value || !(v >= 1 && v <= 99)); flag('price', price, false);
        }
        const start = q('[data-promo-start]'), end = q('[data-promo-end]');
        flag('dates', end, !start?.value || !end?.value || end.value < start.value);
        dialog.querySelectorAll('.promo-date__button').forEach(b => b.toggleAttribute('aria-invalid', b.previousElementSibling?.getAttribute('aria-invalid') === 'true'));
        const first = problems[0];
        if (first === dish) {
            const comboInput = dialog.querySelector('.promo-combo__input');
            if (comboInput) { comboInput.dataset.quietFocus = 'true'; comboInput.focus(); }
        } else first?.focus({ preventScroll: false });
        return problems.length === 0;
    }
    dialog?.addEventListener('submit', async event => {
        const form = event.target.closest('[data-promo-form]');
        if (!form) return;
        event.preventDefault();
        if (!checkForm()) return;
        const button = form.querySelector('button[type=submit]');
        if (button) button.disabled = true;
        try {
            const response = await fetch(form.action || location.href, { method: 'POST', body: new FormData(form), credentials: 'same-origin' });
            if (response.redirected) {                       // saved: show the updated list and the toast
                storage.remove(draftKey());
                // fetch already followed the redirect and used up the TempData message,
                // so keep the toast text and show it after the reload.
                const savedPage = new DOMParser().parseFromString(await response.text(), 'text/html');
                const saved = savedPage.querySelector('.yoru-toast-msg')?.textContent.trim();
                try {
                    sessionStorage.setItem(SCROLL_KEY, String(scrollY));
                    if (saved) sessionStorage.setItem(TOAST_KEY, saved);
                } catch { }
                location.href = response.url; return;
            }
            const next = new DOMParser().parseFromString(await response.text(), 'text/html');
            const fresh = next.querySelector('[data-promo-dialog]');
            if (!fresh) throw new Error('reload');
            const values = readForm();
            dialog.innerHTML = fresh.innerHTML;              // server message (e.g. overlapping dates), same values
            writeForm(values);
            prepareForm();
            snapshot = '';
            (q('[aria-invalid="true"]:not([aria-hidden])') || q('.promo-form__error:not([hidden])'))?.focus?.();
        } catch {
            if (button) button.disabled = false;
            form.submit();
        }
    });

    // Same look as the team toast in the shared layout (gold "info" style).
    // Same look as the team toast in the shared layout (gold "info" or green "success").
    const TOAST_KEY = 'yoru-promo-toast';
    function showToast(message, success = false) {
        document.querySelector('[data-promo-toast]')?.remove();
        const wrap = document.createElement('div');
        wrap.className = 'yoru-toast-wrap'; wrap.dataset.promoToast = '';
        wrap.innerHTML = '<div class="yoru-toast yoru-toast--' + (success ? 'success' : 'info') + '" role="status" aria-live="polite"><span class="yoru-toast-seal" aria-hidden="true"><i class="bi ' + (success ? 'bi-check-lg' : 'bi-info-lg') + '"></i></span>'
            + '<div class="yoru-toast-body"><p class="yoru-toast-title"><span class="yoru-toast-jp">' + (success ? '完了' : 'お知らせ') + '</span>' + (success ? 'All set' : 'Notice') + '</p><p class="yoru-toast-msg"></p></div>'
            + '<button type="button" class="yoru-toast-close" aria-label="Close notification"><i class="bi bi-x-lg"></i></button></div>';
        wrap.querySelector('.yoru-toast-msg').textContent = message;
        wrap.querySelector('.yoru-toast-close').addEventListener('click', () => wrap.remove());
        document.body.append(wrap);
        setTimeout(() => wrap.remove(), 5000);
    }
    try {
        const pending = sessionStorage.getItem(TOAST_KEY);
        if (pending) {
            sessionStorage.removeItem(TOAST_KEY);
            if (!document.querySelector('.yoru-toast-wrap')) showToast(pending, true);
        }
    } catch { }

    // "New promotion" becomes "Resume draft" while a new-promotion draft is saved.
    // New / Edit / Run again links show "Resume draft" while that form has a saved draft.
    function draftKeyForLink(link) {
        const url = new URL(link.href, location.href);
        const edit = url.searchParams.get('Edit'), copy = url.searchParams.get('Copy');
        return DRAFT_PREFIX + (edit ? `edit-${edit}` : copy ? `copy-${copy}` : 'new');
    }
    function refreshNewButtons() {
        root.querySelectorAll('a[data-promo-open]').forEach(link => {
            if (link.classList.contains('promo-next__item')) return;   // "Coming next" cards keep their look
            const hasDraft = !!storage.get(draftKeyForLink(link));
            const label = link.querySelector('span') || link;
            const icon = link.querySelector('i');
            if (link.dataset.label === undefined) { link.dataset.label = label.textContent; link.dataset.icon = icon?.className ?? ''; }
            label.textContent = hasDraft ? 'Resume draft' : link.dataset.label;
            if (icon) icon.className = hasDraft ? 'bi bi-journal-text' : link.dataset.icon;
            link.classList.toggle('has-draft', hasDraft);
        });
    }

    // Small "unsaved changes" prompt shown inside the pop-up.
    function askDraft() {
        return new Promise(resolve => {
            const ask = document.createElement('dialog');
            ask.className = 'promo-confirm promo-draft-ask';
            ask.innerHTML = '<p class="promo-confirm__eyebrow">Unsaved changes</p>'
                + '<button type="button" class="promo-confirm__close" aria-label="Keep editing" data-answer="keep"><i class="bi bi-x-lg" aria-hidden="true"></i></button>'
                + '<h2>Save as a draft?</h2><p>Your changes are not saved yet. Keep them as a draft to continue later, or discard them.</p>'
                + '<div class="promo-confirm__actions"><button type="button" class="promo-confirm__back" data-answer="discard">Discard</button>'
                + '<button type="button" class="promo-confirm__ok" data-answer="draft">Save draft</button></div>';
            dialog.append(ask);
            const done = answer => { ask.close(); ask.remove(); resolve(answer); };
            ask.addEventListener('click', event => { const b = event.target.closest('[data-answer]'); if (b) done(b.dataset.answer); });
            ask.addEventListener('cancel', event => { event.preventDefault(); done('keep'); });
            ask.showModal();
            ask.querySelector('[data-answer="draft"]').focus();
        });
    }

    function prepareForm() {
        restoreDraft();
        const start = q('[data-promo-start]'), end = q('[data-promo-end]');
        if (start && end) end.min = start.value;
        dialog?.querySelectorAll('[data-hint-percent]:not([hidden]), [data-hint-price]:not([hidden]), [data-hint-dates]:not([hidden])').forEach(h => h.dataset.server = 'true');
        enhanceDish(); enhanceDates(); updateKind(); updatePreview();
        snapshot = JSON.stringify(readForm());
        if (q('[data-promo-draft-note]')) snapshot = '';   // a restored draft still counts as unsaved
    }
    function openForm() {
        if (!dialog || typeof dialog.showModal !== 'function') return;
        // The server renders the pop-up already open (for no-JavaScript use); reopen it as a modal.
        if (dialog.open && !dialog.matches(':modal')) dialog.close();
        if (!dialog.open) dialog.showModal();
        prepareForm();
        (q('[aria-invalid="true"]:not([aria-hidden])') || q('[data-promo-title]'))?.focus({ preventScroll: true });
    }
    async function closeForm(force = false) {
        closePicker();
        if (!force && dialog?.open && JSON.stringify(readForm()) !== snapshot) {
            const answer = await askDraft();
            if (answer === 'keep') return;
            if (answer === 'draft') {
                storage.set(draftKey(), { values: readForm(), savedAt: Date.now() });
                showToast('Draft saved. Open the same form again to continue.');
                refreshNewButtons();
            } else {
                const had = !!storage.get(draftKey());
                storage.remove(draftKey()); refreshNewButtons();
                showToast(had ? 'Draft discarded.' : 'Changes discarded.');
            }
        }
        dialog?.close();
        history.replaceState(null, '', listUrl());   // a refresh will not reopen the form
    }
    // New / Edit / Run again: load just the pop-up content, keep the page where it is.
    root.addEventListener('click', async event => {
        const link = event.target.closest('a[data-promo-open]');
        if (!link || !dialog || typeof dialog.showModal !== 'function' || event.ctrlKey || event.metaKey || event.button !== 0) return;
        event.preventDefault();
        try {
            const next = await fetchPage(link.href);
            const fresh = next.querySelector('[data-promo-dialog]');
            if (!fresh) throw new Error('reload');
            dialog.innerHTML = fresh.innerHTML;
            dialog.dataset.mode = fresh.dataset.mode;
            history.replaceState(null, '', link.href);
            openForm();
        } catch { location.href = link.href; }
    });
    dialog?.addEventListener('click', event => {
        if (event.target.closest('[data-promo-close]')) { event.preventDefault(); closeForm(); return; }
        if (event.target === dialog) closeForm();
    });
    dialog?.addEventListener('cancel', event => {
        event.preventDefault();
        if (openPicker) { closePicker(); return; }
        if (dialog.querySelector('.promo-combo.is-open') || dialog.querySelector('.promo-draft-ask')) return;
        closeForm();
    });
    dialog?.addEventListener('input', event => { if (event.target.matches('input')) event.target.dataset.touched = 'true'; updateHints(); });
    dialog?.addEventListener('change', event => {
        if (event.target.matches('[data-promo-kind], [data-promo-dish]')) updateKind();
        if (event.target.matches('[data-promo-kind]')) {
            ['percent', 'price'].forEach(h => { const el = q(`[data-hint-${h}]`); if (el) { el.hidden = true; delete el.dataset.server; } });
            q('[data-promo-percent]')?.removeAttribute('aria-invalid'); q('[data-promo-price]')?.removeAttribute('aria-invalid');
        }
        const start = q('[data-promo-start]'), end = q('[data-promo-end]');
        if (event.target === start && end) {
            end.min = start.value;
            if (end.value && end.value < start.value) { end.value = start.value; end.dispatchEvent(new Event('change')); }
        }
        if (event.target.matches('input')) event.target.dataset.touched = 'true';
        updatePreview(); updateHints();
    });
    dialog?.addEventListener('input', updatePreview);
    refreshNewButtons();
    if (dialog?.dataset.open === 'true') openForm(); else prepareForm();

    // ----- Confirm before Stop / Delete -----
    const confirmDialog = root.querySelector('[data-promo-confirm-dialog]');
    let pending = null;
    root.addEventListener('submit', event => {
        const target = event.target.closest('form[data-promo-confirm]');
        if (!target || target.dataset.confirmed === 'true' || !confirmDialog || typeof confirmDialog.showModal !== 'function') return;
        event.preventDefault();
        pending = target;
        confirmDialog.querySelector('[data-promo-confirm-text]').textContent = target.dataset.promoConfirm;
        confirmDialog.querySelector('[data-promo-confirm-ok]').textContent = target.dataset.promoConfirmAction || 'Confirm';
        confirmDialog.querySelector('[data-promo-confirm-heading]').textContent = target.dataset.promoConfirmTitle || 'Are you sure?';
        confirmDialog.showModal();
        confirmDialog.querySelector('[data-promo-confirm-cancel]').focus();
    });
    confirmDialog?.querySelectorAll('[data-promo-confirm-cancel]').forEach(b => b.addEventListener('click', () => confirmDialog.close()));
    confirmDialog?.addEventListener('click', event => { if (event.target === confirmDialog) confirmDialog.close(); });
    confirmDialog?.querySelector('[data-promo-confirm-ok]')?.addEventListener('click', event => {
        if (!pending) return;
        event.currentTarget.disabled = true;
        pending.dataset.confirmed = 'true';
        try { sessionStorage.setItem(SCROLL_KEY, String(scrollY)); } catch { }
        pending.requestSubmit ? pending.requestSubmit() : pending.submit();
    });
    confirmDialog?.addEventListener('close', () => { pending = null; confirmDialog.querySelector('[data-promo-confirm-ok]').disabled = false; });
})();
