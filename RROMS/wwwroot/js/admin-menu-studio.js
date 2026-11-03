/* Owner: Member 3. Admin Menu studio (/Admin/Menu). Replaces the six older menu scripts.
   - Tabs, live search, dropdowns, chips and paging update the page in place (no reload).
   - New / Edit open a pop-up with a live preview. Closing with unsaved changes asks to keep a draft;
     drafts live in this browser (localStorage) and the matching button shows "Resume draft".
   - Save, Delete and Save order post in the background, then show the updated list and the toast.
   Without JavaScript every link and form still works as a normal page load. */
(() => {
    'use strict';
    const root = document.querySelector('[data-ms-page]');
    if (!root) return;

    const formKeys = ['New', 'editId', 'itemEditId'];
    const SCROLL_KEY = 'yoru-menu-scroll';
    const TOAST_KEY = 'yoru-menu-toast';
    const DRAFT_PREFIX = 'yoru-menu-draft:';
    const IMAGE_TYPES = ['image/jpeg', 'image/png', 'image/webp'];
    const MAX_IMAGE = 5 * 1024 * 1024;
    const money = value => (Math.round((Number(value) || 0) * 100) / 100)
        .toLocaleString('en-MY', { minimumFractionDigits: 2, maximumFractionDigits: 2 });

    const storage = {
        get(key) { try { return JSON.parse(localStorage.getItem(DRAFT_PREFIX + key) || 'null'); } catch { return null; } },
        set(key, value) { try { localStorage.setItem(DRAFT_PREFIX + key, JSON.stringify(value)); } catch { } },
        remove(key) { try { localStorage.removeItem(DRAFT_PREFIX + key); } catch { } }
    };

    // ----- Toasts: same look as the team toast in the shared layout -----
    const toastLook = {
        success: ['All set', '完了', 'bi-check-lg'],
        error: ['Oops', '注意', 'bi-exclamation-lg'],
        info: ['Notice', 'お知らせ', 'bi-info-lg']
    };
    function showToast(message, kind = 'info') {
        if (!message) return;
        document.querySelectorAll('[data-ms-toast], .yoru-toast-wrap').forEach(t => t.remove());
        const [title, jp, icon] = toastLook[kind] || toastLook.info;
        const wrap = document.createElement('div');
        wrap.className = 'yoru-toast-wrap';
        wrap.dataset.msToast = '';
        wrap.innerHTML = `<div class="yoru-toast yoru-toast--${kind}" role="${kind === 'error' ? 'alert' : 'status'}" aria-live="polite">`
            + `<span class="yoru-toast-seal" aria-hidden="true"><i class="bi ${icon}"></i></span>`
            + `<div class="yoru-toast-body"><p class="yoru-toast-title"><span class="yoru-toast-jp">${jp}</span>${title}</p><p class="yoru-toast-msg"></p></div>`
            + '<button type="button" class="yoru-toast-close" aria-label="Close notification"><i class="bi bi-x-lg"></i></button></div>';
        wrap.querySelector('.yoru-toast-msg').textContent = message;
        wrap.querySelector('.yoru-toast-close').addEventListener('click', () => wrap.remove());
        document.body.append(wrap);
        setTimeout(() => wrap.remove(), 5000);
    }
    // The team toast inside a page we fetched (its TempData was used up by that fetch).
    function toastFrom(doc) {
        const toast = doc.querySelector('[data-toast]');
        if (!toast) return null;
        const kind = toast.classList.contains('yoru-toast--error') ? 'error'
            : toast.classList.contains('yoru-toast--success') ? 'success' : 'info';
        return { message: toast.querySelector('.yoru-toast-msg')?.textContent.trim(), kind };
    }

    // After a fallback reload, restore the scroll position and any toast we carried over.
    try {
        const saved = sessionStorage.getItem(SCROLL_KEY);
        if (saved !== null) { sessionStorage.removeItem(SCROLL_KEY); scrollTo(0, Number(saved) || 0); }
        const toast = JSON.parse(sessionStorage.getItem(TOAST_KEY) || 'null');
        if (toast) { sessionStorage.removeItem(TOAST_KEY); showToast(toast.message, toast.kind); }
    } catch { /* storage unavailable */ }
    function reloadTo(url, toast) {
        try {
            sessionStorage.setItem(SCROLL_KEY, String(scrollY));
            if (toast?.message) sessionStorage.setItem(TOAST_KEY, JSON.stringify(toast));
        } catch { }
        location.href = url;
    }

    const listUrl = (from = location.href) => {
        const url = new URL(from, location.href);
        formKeys.forEach(key => url.searchParams.delete(key));
        return url.pathname + url.search;
    };
    const parse = async response => new DOMParser().parseFromString(await response.text(), 'text/html');
    const fetchPage = async (url, signal) => {
        const response = await fetch(url, { signal, credentials: 'same-origin', headers: { 'X-Requested-With': 'XMLHttpRequest' } });
        if (!response.ok) throw new Error('reload');
        return parse(response);
    };
    const dialogSlot = () => root.querySelector('[data-ms-swap="dialog"]');
    const editor = () => root.querySelector('[data-ms-dialog]');

    // ----- In-place page updates -----
    // Photos already on screen are reused so nothing blinks; the search box keeps focus while typing.
    function swapFrom(doc) {
        const search = root.querySelector('[data-ms-search]');
        const typing = search && document.activeElement === search
            ? { name: search.name, value: search.value, start: search.selectionStart, end: search.selectionEnd } : null;
        const photos = new Map();
        root.querySelectorAll('[data-ms-swap] img').forEach(img => {
            const key = img.getAttribute('src');
            if (!photos.has(key)) photos.set(key, []);
            photos.get(key).push(img);
        });
        root.querySelectorAll('[data-ms-swap]').forEach(part => {
            const fresh = doc.querySelector(`[data-ms-swap="${part.dataset.msSwap}"]`);
            if (!fresh) return;
            const incoming = document.importNode(fresh, true);
            incoming.querySelectorAll('img').forEach(img => {
                const kept = photos.get(img.getAttribute('src'))?.shift();
                if (kept && !kept.closest('[data-ms-dialog]')) img.replaceWith(kept);
            });
            part.replaceWith(incoming);
        });
        const freshRoot = doc.querySelector('[data-ms-page]');
        if (freshRoot) root.dataset.msView = freshRoot.dataset.msView;
        if (doc.title) document.title = doc.title;
        if (typing) {
            const box = root.querySelector(`[data-ms-search][name="${typing.name}"]`);
            if (box) {
                box.value = typing.value;
                box.focus({ preventScroll: true });
                try { box.setSelectionRange(typing.start, typing.end); } catch { }
            }
        }
        refreshDraftLabels();
        enhanceDialog();
    }

    let request = null, searchTimer = null;
    async function navigate(href, { push = true, replace = false, toTop = false } = {}) {
        const url = new URL(href, location.href);
        formKeys.forEach(key => url.searchParams.delete(key));
        request?.abort();
        const current = new AbortController(); request = current;
        const workspace = () => root.querySelector('[data-ms-swap="workspace"]');
        const dimTimer = setTimeout(() => workspace()?.classList.add('is-loading'), 250);
        root.setAttribute('aria-busy', 'true');
        try {
            const doc = await fetchPage(url, current.signal);
            if (request !== current) return;
            if (editor()?.open) editor().close();
            swapFrom(doc);
            if (replace) history.replaceState(null, '', url); else if (push) history.pushState(null, '', url);
            announce();
            if (toTop) document.getElementById('ms-collection')?.scrollIntoView({ behavior: 'smooth', block: 'start' });
        } catch (error) {
            if (error.name !== 'AbortError') location.href = url;   // fall back to a normal page load
        } finally {
            clearTimeout(dimTimer);
            if (request === current) {
                request = null;
                root.removeAttribute('aria-busy');
                workspace()?.classList.remove('is-loading');
            }
        }
    }
    function announce() {
        const live = root.querySelector('[data-ms-live]');
        const summary = root.querySelector('.ms-pager > span, .ms-empty h3');
        if (live && summary) live.textContent = summary.textContent.trim();
    }

    root.addEventListener('click', async event => {
        const link = event.target.closest('a[data-ms-nav]');
        if (!link || event.ctrlKey || event.metaKey || event.shiftKey || event.button !== 0) return;
        event.preventDefault();
        link.closest('details')?.removeAttribute('open');
        // Leaving reorder mode (Cancel, the tabs, ...) with a new order that is not saved yet.
        const order = reorderForm();
        if (order && reorderChanged()) {
            const answer = await ask({
                eyebrow: 'Unsaved order', title: 'Save the new order?',
                text: 'You moved some categories but have not saved yet. Save the new order, or discard it and keep the current one.',
                yes: 'Save order', no: 'Discard'
            });
            if (answer === 'cancel') return;   // the × keeps reordering
            if (answer === 'yes') { order.dataset.saving = 'true'; post(order); return; }
            showToast('New order discarded.');
        }
        if (link.matches('[data-ms-search-clear]')) {
            const box = root.querySelector('[data-ms-search]');
            if (box) box.value = '';
        }
        navigate(link.href, { toTop: link.matches('[data-ms-top]') });
    });
    addEventListener('popstate', () => {
        const url = new URL(location.href);
        if (formKeys.some(key => url.searchParams.has(key))) return;
        navigate(location.href, { push: false });
    });

    // Live search: results update while typing, after a short pause. Enter searches at once.
    function searchUrl(form) {
        const url = new URL(form.getAttribute('action') || location.pathname, location.href);
        url.search = '';
        new FormData(form).forEach((value, key) => {
            if (key === '__RequestVerificationToken') return;
            const text = String(value).trim();
            if (text) url.searchParams.set(key, text);
        });
        return url;
    }
    root.addEventListener('input', event => {
        const box = event.target.closest('[data-ms-search]');
        if (!box) return;
        const clear = box.closest('form')?.querySelector('[data-ms-search-clear]');
        if (clear) clear.hidden = !box.value.trim();
        clearTimeout(searchTimer);
        searchTimer = setTimeout(() => navigate(searchUrl(box.form), { replace: true }), 350);
    });
    root.addEventListener('submit', event => {
        const form = event.target.closest('form[data-ms-filters]');
        if (!form) return;
        event.preventDefault();
        clearTimeout(searchTimer);
        navigate(searchUrl(form));
    });

    // Dropdowns: close when clicking elsewhere or pressing Escape; only one open at a time.
    document.addEventListener('click', event => {
        root.querySelectorAll('details[data-ms-pick][open]').forEach(d => { if (!d.contains(event.target)) d.removeAttribute('open'); });
    });
    root.addEventListener('toggle', event => {
        if (event.target.matches?.('details[data-ms-pick]') && event.target.open) {
            root.querySelectorAll('details[data-ms-pick][open]').forEach(d => { if (d !== event.target) d.removeAttribute('open'); });
        }
    }, true);
    root.addEventListener('keydown', event => {
        const open = event.target.closest?.('details[data-ms-pick][open]');
        if (event.key === 'Escape' && open) { event.preventDefault(); open.removeAttribute('open'); open.querySelector('summary').focus(); }
    });

    // ----- Drafts and "Resume draft" labels -----
    function refreshDraftLabels() {
        root.querySelectorAll('[data-ms-draft-key]').forEach(link => {
            const hasDraft = !!storage.get(link.dataset.msDraftKey);
            const label = link.querySelector('[data-ms-draft-label]');
            const icon = link.querySelector('i');
            if (link.dataset.label === undefined) {
                link.dataset.label = label?.textContent ?? '';
                link.dataset.icon = icon?.className ?? '';
            }
            if (label) label.textContent = hasDraft ? 'Resume draft' : link.dataset.label;
            if (icon) icon.className = hasDraft ? 'bi bi-journal-text' : link.dataset.icon;
            link.classList.toggle('has-draft', hasDraft);
            if (link.dataset.title === undefined) link.dataset.title = link.getAttribute('title') ?? '';
            if (hasDraft) link.title = 'Resume draft: you have an unsaved draft here';
            else if (link.dataset.title) link.title = link.dataset.title; else link.removeAttribute('title');
        });
    }
    const draftFields = form => [...form.elements].filter(el => el.name
        && (el.name.startsWith('Input.') || el.name.startsWith('ItemInput.') || el.name === 'RemoveItemImage')
        && el.type !== 'hidden' && el.type !== 'file');
    function readForm(form) {
        const values = {};
        draftFields(form).forEach(el => { values[el.name] = el.type === 'checkbox' ? el.checked : el.value; });
        return values;
    }
    function writeForm(form, values) {
        draftFields(form).forEach(el => {
            if (!(el.name in values)) return;
            if (el.type === 'checkbox') el.checked = !!values[el.name]; else el.value = values[el.name];
        });
    }

    // ----- Pop-up editor -----
    let snapshot = '';
    const q = selector => editor()?.querySelector(selector);
    const form = () => q('[data-ms-form]');
    const hasServerErrors = () => !!(q('.ms-alert[role=alert]:not([hidden])') || q('.field-validation-error') || q('.input-validation-error'));

    function updatePreview() {
        const dialog = editor();
        if (!dialog) return;
        const name = q('[data-ms-name]');
        if (dialog.dataset.msKind === 'category') {
            const value = name?.value ?? '';
            const [first, ...rest] = value.split('·');
            q('[data-ms-count]').textContent = value.length;
            q('[data-ms-preview-name]').textContent = first.trim() || 'Your category';
            q('[data-ms-preview-sub]').textContent = rest.join('·').trim() || ' ';
            return;
        }
        q('[data-ms-count]').textContent = name?.value.length ?? 0;
        q('[data-ms-preview-name]').textContent = name?.value.trim() || 'Dish name';
        const desc = q('[data-ms-desc]');
        q('[data-ms-desc-count]').textContent = desc?.value.length ?? 0;
        q('[data-ms-preview-desc]').textContent = desc?.value.trim() || 'A short description appears here.';
        q('[data-ms-preview-price]').textContent = money(q('[data-ms-price]')?.value);
        const select = q('[data-ms-category]');
        const option = select?.selectedOptions[0];
        q('[data-ms-preview-category]').textContent = select?.value ? option.textContent.split('·')[0].trim() : 'Category';
        const note = q('[data-ms-code-note]');
        if (note) note.hidden = !select?.dataset.original || select.value === select.dataset.original;
        const available = q('[data-ms-available]')?.checked ?? true;
        q('[data-ms-preview-card]')?.classList.toggle('is-off', !available);
        const off = q('[data-ms-preview-off]'); if (off) off.hidden = available;
        const badge = q('[data-ms-preview-avail]');
        if (badge) {
            badge.classList.toggle('is-off', !available);
            badge.querySelector('span').textContent = available ? 'Available' : 'Unavailable';
        }
        updatePhoto();
    }

    let photoUrl = null;
    function updatePhoto() {
        const input = q('[data-ms-image]'), img = q('[data-ms-preview-photo]'), empty = q('[data-ms-preview-nophoto]');
        if (!input || !img || !empty) return;
        const error = q('[data-ms-image-error]'), state = q('[data-ms-photo-state]'), title = q('[data-ms-drop-title]');
        const remove = q('[data-ms-remove-image]');
        const file = input.files?.[0];
        if (photoUrl) { URL.revokeObjectURL(photoUrl); photoUrl = null; }
        let problem = '';
        if (file && !IMAGE_TYPES.includes(file.type)) problem = 'Choose a JPG, PNG or WebP image.';
        else if (file && file.size > MAX_IMAGE) problem = 'Choose an image no larger than 5 MB.';
        if (error && (problem || error.dataset.client)) { error.textContent = problem; error.dataset.client = problem ? 'true' : ''; }
        if (problem) { input.value = ''; }
        const chosen = !problem && file;
        if (chosen) { photoUrl = URL.createObjectURL(file); img.src = photoUrl; }
        else if (img.dataset.current && !remove?.checked) img.src = img.dataset.current;
        const show = chosen || (img.dataset.current && !remove?.checked);
        img.hidden = !show; empty.hidden = !!show;
        if (remove) remove.disabled = !!chosen;
        if (title) title.textContent = chosen ? file.name : (img.dataset.current ? 'Drop a new photo or click to replace' : 'Drop a photo or click to choose');
        if (state) state.textContent = chosen ? 'New photo selected. It is uploaded when you save.'
            : remove?.checked ? 'The current photo will be removed when you save.'
                : 'This is how the dish appears on the customer menu.';
    }

    // Category field in the menu item pop-up: a styled dropdown (same look as the page's sort
    // dropdown) over the real <select>, which stays in the form and still submits the value.
    function enhanceSelect() {
        const wrap = q('[data-ms-select]');
        if (!wrap) return;
        const select = wrap.querySelector('select');
        let pick = wrap.querySelector('details[data-ms-pick]');
        if (!pick) {
            pick = document.createElement('details');
            pick.className = 'ms-pick ms-pick--field';
            pick.dataset.msPick = '';
            pick.innerHTML = '<summary><i class="bi bi-grid ms-pick__icon" aria-hidden="true"></i><span></span>'
                + '<i class="bi bi-chevron-down ms-pick__chev" aria-hidden="true"></i></summary>'
                + '<div class="ms-pick__menu ms-pick__menu--scroll" role="listbox"></div>';
            pick.querySelector('summary').setAttribute('aria-label', 'Category');
            const menu = pick.querySelector('.ms-pick__menu');
            [...select.options].forEach(option => {
                if (!option.value) return;
                const button = document.createElement('button');
                button.type = 'button';
                button.dataset.value = option.value;
                button.setAttribute('role', 'option');
                button.innerHTML = '<span></span><i class="bi bi-check-lg" aria-hidden="true"></i>';
                button.querySelector('span').textContent = option.textContent;
                menu.append(button);
            });
            menu.addEventListener('click', event => {
                const button = event.target.closest('button[data-value]');
                if (!button) return;
                select.value = button.dataset.value;
                select.dispatchEvent(new Event('change', { bubbles: true }));
                pick.removeAttribute('open');
                pick.querySelector('summary').focus();
            });
            select.addEventListener('change', () => syncSelect(wrap));
            select.classList.add('ms-select__native');
            select.tabIndex = -1;
            wrap.querySelector(':scope > i.bi-chevron-down')?.remove();
            wrap.append(pick);
        }
        syncSelect(wrap);
    }
    function syncSelect(wrap) {
        const select = wrap.querySelector('select'), pick = wrap.querySelector('details[data-ms-pick]');
        if (!select || !pick) return;
        pick.querySelector('summary span').textContent = select.value ? select.selectedOptions[0].textContent : 'Select a category';
        pick.classList.toggle('is-placeholder', !select.value);
        pick.classList.toggle('is-invalid', select.classList.contains('input-validation-error'));
        pick.querySelectorAll('button[data-value]').forEach(button => {
            const on = button.dataset.value === select.value;
            button.classList.toggle('is-on', on);
            button.setAttribute('aria-selected', String(on));
            button.querySelector('i').hidden = !on;
        });
    }

    function prepareForm({ restore = true } = {}) {
        const f = form();
        if (!f) return;
        const key = editor().dataset.msDraft;
        const draft = restore && !hasServerErrors() ? storage.get(key) : null;
        const note = q('[data-ms-draft-note]');
        if (draft) { writeForm(f, draft.values || {}); if (note) note.hidden = false; }
        enhanceSelect();
        updatePreview();
        snapshot = JSON.stringify(readForm(f));
        if (draft || hasServerErrors()) snapshot = '';   // a restored draft or a failed save still counts as unsaved
    }
    function openDialog(options) {
        const dialog = editor();
        if (!dialog || typeof dialog.showModal !== 'function') return;
        // The server renders the pop-up already open (for no-JavaScript use); reopen it as a modal.
        if (dialog.open && !dialog.matches(':modal')) dialog.close();
        if (!dialog.open) dialog.showModal();
        prepareForm(options);
        (q('.input-validation-error') || q('[data-ms-name]'))?.focus({ preventScroll: true });
    }
    function enhanceDialog() {
        const dialog = editor();
        if (!dialog) return;
        if (dialog.dataset.open === 'true' && typeof dialog.showModal === 'function') openDialog();
        else if (dialog.open) dialog.close();
    }

    // Small confirm pop-up, same look as the Promotions "Save as a draft?" dialog.
    function ask({ eyebrow, title, text, yes, no }) {
        return new Promise(resolve => {
            const box = document.createElement('dialog');
            box.className = 'ms-confirm';
            box.innerHTML = '<p class="ms-confirm__eyebrow"></p>'
                + '<button type="button" class="ms-confirm__close" aria-label="Keep editing" data-answer="cancel"><i class="bi bi-x-lg" aria-hidden="true"></i></button>'
                + '<h2></h2><p class="ms-confirm__text"></p>'
                + '<div class="ms-confirm__actions"><button type="button" class="ms-confirm__back" data-answer="no"></button>'
                + '<button type="button" class="ms-confirm__ok" data-answer="yes"></button></div>';
            box.querySelector('.ms-confirm__eyebrow').textContent = eyebrow;
            box.querySelector('h2').textContent = title;
            box.querySelector('.ms-confirm__text').textContent = text;
            box.querySelector('[data-answer=no]').textContent = no;
            box.querySelector('[data-answer=yes]').textContent = yes;
            root.append(box);
            const done = answer => { box.close(); box.remove(); resolve(answer); };
            box.addEventListener('click', event => {
                const button = event.target.closest('[data-answer]');
                if (button) done(button.dataset.answer); else if (event.target === box) done('cancel');
            });
            box.addEventListener('cancel', event => { event.preventDefault(); done('cancel'); });
            box.showModal();
            box.querySelector('[data-answer=yes]').focus();
        });
    }

    async function closeDialog(force = false) {
        const dialog = editor(), f = form();
        if (!dialog?.open) return;
        if (!force && f && JSON.stringify(readForm(f)) !== snapshot) {
            const answer = await ask({
                eyebrow: 'Unsaved changes', title: 'Save as a draft?',
                text: 'Your changes are not saved yet. Keep them as a draft to continue later, or discard them.',
                yes: 'Save draft', no: 'Discard'
            });
            if (answer === 'cancel') return;   // the × keeps editing
            const key = dialog.dataset.msDraft;
            if (answer === 'yes') {
                storage.set(key, { values: readForm(f), savedAt: Date.now() });
                showToast('Draft saved. Open the same form again to continue.');
            } else {
                const had = !!storage.get(key);
                storage.remove(key);
                showToast(had ? 'Draft discarded.' : 'Changes discarded.');
            }
            refreshDraftLabels();
        }
        dialog.close();
        if (photoUrl) { URL.revokeObjectURL(photoUrl); photoUrl = null; }
        history.replaceState(null, '', listUrl());   // a refresh will not reopen the pop-up
    }

    // New / Edit: load just the pop-up, keep the page where it is.
    root.addEventListener('click', async event => {
        const link = event.target.closest('a[data-ms-open]');
        if (!link || event.ctrlKey || event.metaKey || event.shiftKey || event.button !== 0) return;
        event.preventDefault();
        if (link.getAttribute('aria-disabled') === 'true') return;
        if (typeof HTMLDialogElement !== 'function') { location.href = link.href; return; }
        try {
            const doc = await fetchPage(link.href);
            const fresh = doc.querySelector('[data-ms-swap="dialog"]');
            if (!fresh?.querySelector('[data-ms-dialog]')) throw new Error('reload');
            if (editor()?.open) editor().close();
            dialogSlot().replaceWith(document.importNode(fresh, true));
            history.replaceState(null, '', link.href);
            openDialog();
        } catch { location.href = link.href; }
    });
    root.addEventListener('click', event => {
        const dialog = editor();
        if (!dialog || !dialog.contains(event.target)) return;
        if (event.target.closest('[data-ms-close]')) { event.preventDefault(); closeDialog(); return; }
        if (event.target === dialog) closeDialog();   // click on the backdrop
    });
    root.addEventListener('cancel', event => {
        if (event.target !== editor()) return;
        event.preventDefault();
        const openPick = editor().querySelector('details[data-ms-pick][open]');
        if (openPick) { openPick.removeAttribute('open'); openPick.querySelector('summary').focus(); return; }
        closeDialog();
    }, true);
    root.addEventListener('input', event => { if (editor()?.contains(event.target)) updatePreview(); });
    root.addEventListener('change', event => {
        if (!editor()?.contains(event.target)) return;
        if (event.target.matches('[data-ms-image], [data-ms-remove-image]')) updatePhoto(); else updatePreview();
    });
    ['dragenter', 'dragover'].forEach(type => root.addEventListener(type, event => {
        event.target.closest?.('[data-ms-drop]')?.classList.add('is-over');
    }));
    ['dragleave', 'drop'].forEach(type => root.addEventListener(type, event => {
        event.target.closest?.('[data-ms-drop]')?.classList.remove('is-over');
    }));

    // Quick checks before posting (the server checks everything again).
    function setError(f, field, message) {
        const span = f.querySelector(`[data-valmsg-for="${field}"]`);
        const input = f.elements[field];
        if (span) { span.textContent = message; span.classList.toggle('field-validation-error', !!message); }
        if (input) input.classList.toggle('input-validation-error', !!message);
        const wrap = input?.closest?.('[data-ms-select]');
        if (wrap) syncSelect(wrap);
    }
    function checkForm(f) {
        const problems = [];
        const check = (field, bad, message) => {
            if (!f.elements[field]) return;
            setError(f, field, bad ? message : '');
            if (bad) problems.push(f.elements[field]);
        };
        if (f.elements['Input.Name']) {
            const name = f.elements['Input.Name'].value.trim();
            check('Input.Name', !name || name.length > 50, 'Enter a category name of 1 to 50 characters.');
        }
        if (f.elements['ItemInput.Name']) {
            const name = f.elements['ItemInput.Name'].value.trim();
            check('ItemInput.CategoryId', !f.elements['ItemInput.CategoryId'].value, 'Select a category.');
            check('ItemInput.Name', !name || name.length > 100, 'Enter a menu item name of 1 to 100 characters.');
            const price = Number(f.elements['ItemInput.Price'].value);
            check('ItemInput.Price', !(price >= 0.01 && price <= 10000) || Math.abs(Math.round(price * 100) - price * 100) > 1e-6,
                'Enter a price from RM0.01 to RM10,000.00, with at most two decimal places.');
        }
        problems[0]?.focus();
        return problems.length === 0;
    }

    // Post a form in the background. A redirect means success: show the new list and the toast.
    async function post(f, { onPage } = {}) {
        const button = f.querySelector('button[type=submit]') || document.querySelector(`button[type=submit][form="${f.id}"]`);
        if (button) button.disabled = true;
        try {
            const response = await fetch(f.action || location.href, { method: 'POST', body: new FormData(f), credentials: 'same-origin' });
            if (!response.ok) throw new Error('reload');
            const doc = await parse(response);
            if (response.redirected) {
                const toast = toastFrom(doc);
                try {
                    if (editor()?.open) editor().close();
                    swapFrom(doc);
                    history.replaceState(null, '', listUrl(response.url));
                    if (toast) showToast(toast.message, toast.kind);
                    announce();
                    root.querySelector('.ms-strip.is-new')?.scrollIntoView({ behavior: 'smooth', block: 'center' });
                } catch { reloadTo(response.url, toast); }
                return true;
            }
            if (onPage) onPage(doc); else throw new Error('reload');
            return false;
        } catch {
            reloadTo(location.href);   // something unexpected: reload and let the server show the state
            return false;
        } finally {
            if (button?.isConnected) button.disabled = false;
        }
    }

    root.addEventListener('submit', async event => {
        const f = event.target.closest('form[data-ms-form]');
        if (!f) return;
        event.preventDefault();
        if (!checkForm(f)) return;
        const key = editor().dataset.msDraft;
        const saved = await post(f, {
            onPage: doc => {                      // the server rejected it: show its messages, keep the values
                const fresh = doc.querySelector('[data-ms-dialog]');
                if (!fresh) throw new Error('reload');
                const values = readForm(f);
                editor().innerHTML = fresh.innerHTML;
                writeForm(form(), values);
                prepareForm({ restore: false });
                snapshot = '';
                (q('.input-validation-error') || q('.ms-alert:not([hidden])'))?.focus?.();
            }
        });
        if (saved) { storage.remove(key); refreshDraftLabels(); }
    });

    // ----- Delete: confirm, then post in the background -----
    root.addEventListener('submit', async event => {
        const f = event.target.closest('form[data-ms-delete]');
        if (!f) return;
        event.preventDefault();
        const isItem = !!f.querySelector('input[name="MenuView"]');
        const answer = await ask({
            eyebrow: isItem ? 'Delete dish' : 'Delete category',
            title: isItem ? 'Delete this dish?' : 'Delete this category?',
            text: `“${f.dataset.msDelete}” will be removed from the menu. This cannot be undone.`,
            yes: 'Delete', no: 'Keep it'
        });
        if (answer !== 'yes') return;
        const id = f.querySelector('input[name=id]')?.value;
        if (await post(f) && id) storage.remove(isItem ? `item-edit-${id}` : `cat-edit-${id}`);
        refreshDraftLabels();
    });

    // ----- Reorder: drag the tiles or use the arrow buttons, then Save order -----
    function renumber(list) {
        const tiles = [...list.querySelectorAll('[data-ms-reorder-item]')];
        tiles.forEach((tile, index) => {
            tile.querySelector('[data-ms-position]').textContent = String(index + 1).padStart(2, '0');
            tile.querySelector('[data-ms-move="up"]').disabled = index === 0;
            tile.querySelector('[data-ms-move="down"]').disabled = index === tiles.length - 1;
        });
        const changed = tiles.some(tile => tile.dataset.start !== undefined && Number(tile.dataset.start) !== tiles.indexOf(tile));
        const bar = list.closest('form')?.querySelector('.ms-reorder-bar');
        bar?.classList.toggle('is-changed', changed);
        const status = list.closest('form')?.querySelector('[data-ms-reorder-status]');
        if (status) status.textContent = changed
            ? 'New order ready. Select Save order to apply it, or Cancel to keep the current order.'
            : 'Drag the tiles into the order customers should see. Nothing changes until you save.';
        tiles.forEach((tile, index) => tile.classList.toggle('is-moved', Number(tile.dataset.start) !== index));
    }
    function setupReorder() {
        const list = root.querySelector('[data-ms-reorder-list]');
        if (!list || list.dataset.ready) return;
        list.dataset.ready = 'true';
        [...list.children].forEach((tile, index) => { tile.dataset.start = index; });
        renumber(list);
    }
    let dragged = null;
    root.addEventListener('dragstart', event => {
        const tile = event.target.closest?.('[data-ms-reorder-item]');
        if (!tile) return;
        dragged = tile;
        tile.classList.add('is-dragging');
        event.dataTransfer.effectAllowed = 'move';
        try { event.dataTransfer.setData('text/plain', ''); } catch { }
    });
    root.addEventListener('dragover', event => {
        if (!dragged) return;
        const over = event.target.closest?.('[data-ms-reorder-item]');
        event.preventDefault();
        if (!over || over === dragged) return;
        const box = over.getBoundingClientRect();
        const after = event.clientY > box.bottom - box.height / 4
            || (event.clientY > box.top + box.height / 4 && event.clientX > box.left + box.width / 2);
        over.parentNode.insertBefore(dragged, after ? over.nextSibling : over);
    });
    root.addEventListener('dragend', () => {
        if (!dragged) return;
        dragged.classList.remove('is-dragging');
        const list = dragged.closest('[data-ms-reorder-list]');
        dragged = null;
        if (list) renumber(list);
    });
    root.addEventListener('click', event => {
        const button = event.target.closest('[data-ms-move]');
        if (!button) return;
        const tile = button.closest('[data-ms-reorder-item]'), list = tile.parentNode;
        if (button.dataset.msMove === 'up' && tile.previousElementSibling) list.insertBefore(tile, tile.previousElementSibling);
        if (button.dataset.msMove === 'down' && tile.nextElementSibling) list.insertBefore(tile.nextElementSibling, tile);
        renumber(list);
        const again = tile.querySelector(`[data-ms-move="${button.dataset.msMove}"]`);
        (again.disabled ? tile.querySelector('[data-ms-move]:not(:disabled)') : again)?.focus();
    });
    root.addEventListener('submit', event => {
        const f = event.target.closest('form[data-ms-reorder]');
        if (!f) return;
        event.preventDefault();
        f.dataset.saving = 'true';
        post(f);
    });
    const reorderForm = () => root.querySelector('form[data-ms-reorder]');
    const reorderChanged = () => [...(reorderForm()?.querySelectorAll('[data-ms-reorder-item]') ?? [])]
        .some((tile, index) => tile.dataset.start !== undefined && Number(tile.dataset.start) !== index);
    // Closing or refreshing the browser tab with an unsaved order: the browser asks first.
    addEventListener('beforeunload', event => {
        if (reorderForm() && reorderChanged() && !reorderForm().dataset.saving) { event.preventDefault(); event.returnValue = ''; }
    });
    new MutationObserver(setupReorder).observe(root, { childList: true, subtree: true });

    // ----- Start -----
    refreshDraftLabels();
    setupReorder();
    enhanceDialog();
    root.querySelector('.ms-strip.is-new')?.scrollIntoView({ block: 'center' });
})();
