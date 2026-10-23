/* Owner: Member 3. Progressive enhancement: ordinary GET forms/links also work without JS. */
(() => {
    'use strict';
    const root = document.querySelector('[data-customer-menu]');
    if (!root || root.dataset.enhanced) return;
    root.dataset.enhanced = 'true';
    const status = root.querySelector('[data-menu-status]');
    const dialog = root.querySelector('[data-dish-dialog]');
    let request = null;
    let opener = null;
    const browser = () => root.querySelector('[data-menu-browser]');
    const reducedMotion = () => window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    const announce = message => { if (status) { status.textContent = message; } };
    const normalClick = event => event.button === 0 && !event.ctrlKey && !event.metaKey && !event.shiftKey && !event.altKey;
    let sortTypeahead = '';
    let sortTypeaheadTime = 0;

    function closeSort(restoreFocus = false) {
        const picker = browser()?.querySelector('[data-menu-sort-picker]');
        if (!picker) return;
        const trigger = picker.querySelector('[data-menu-sort-trigger]');
        picker.querySelector('[data-menu-sort-list]').hidden = true;
        trigger.setAttribute('aria-expanded', 'false');
        if (restoreFocus) trigger.focus({ preventScroll: true });
    }

    function openSort(trigger, edge = null) {
        closeSort();
        const picker = trigger.closest('[data-menu-sort-picker]');
        const list = picker.querySelector('[data-menu-sort-list]');
        list.hidden = false;
        trigger.setAttribute('aria-expanded', 'true');
        list.removeAttribute('data-side');
        const rect = trigger.getBoundingClientRect();
        if (window.innerHeight - rect.bottom < list.offsetHeight + 12 && rect.top > list.offsetHeight + 12) list.dataset.side = 'up';
        const options = Array.from(list.querySelectorAll('[data-menu-sort-value]'));
        const selected = options.find(option => option.getAttribute('aria-selected') === 'true');
        (edge === 'last' ? options.at(-1) : edge === 'first' ? options[0] : selected || options[0])?.focus({ preventScroll: true });
        sortTypeahead = '';
    }

    function prepareSortPicker() {
        const select = browser()?.querySelector('#menu-sort');
        const wrapper = select?.closest('.menu-search__sort');
        if (!select || !wrapper || wrapper.querySelector('[data-menu-sort-picker]')) return;
        const cleanLabel = option => option.textContent.replace(/^Sort by:\s*/i, '').trim();
        const selected = select.options[select.selectedIndex]
            || Array.from(select.options).find(option => option.hasAttribute('selected'))
            || select.options[0];
        if (!selected) return;
        select.value = selected.value;
        const picker = document.createElement('div');
        picker.className = 'menu-sort-picker';
        picker.dataset.menuSortPicker = '';
        const trigger = document.createElement('button');
        trigger.type = 'button';
        trigger.id = 'menu-sort-trigger';
        trigger.className = 'menu-sort-trigger';
        trigger.dataset.menuSortTrigger = '';
        trigger.setAttribute('aria-haspopup', 'listbox');
        trigger.setAttribute('aria-expanded', 'false');
        trigger.setAttribute('aria-controls', 'menu-sort-options');
        trigger.setAttribute('aria-label', 'Arrange dishes by: ' + cleanLabel(selected));
        // Only a fixed decorative SVG is HTML; all dynamic labels use textContent.
        trigger.innerHTML = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" aria-hidden="true"><path d="M3 6h18M3 12h18M3 18h18M8 3v6M16 9v6M9 15v6" /></svg>';
        const text = document.createElement('span');
        text.className = 'menu-sort-trigger__text';
        const label = document.createElement('small');
        label.textContent = 'ARRANGE BY';
        const value = document.createElement('span');
        value.className = 'menu-sort-trigger__value';
        value.textContent = cleanLabel(selected);
        text.append(label, value);
        const chevron = document.createElement('span');
        chevron.className = 'menu-sort-trigger__chevron';
        chevron.setAttribute('aria-hidden', 'true');
        trigger.append(text, chevron);
        const list = document.createElement('div');
        list.id = 'menu-sort-options';
        list.className = 'menu-sort-options';
        list.dataset.menuSortList = '';
        list.hidden = true;
        list.setAttribute('role', 'listbox');
        list.setAttribute('aria-label', 'Arrange dishes');
        const heading = document.createElement('p');
        heading.className = 'menu-sort-options__heading';
        heading.textContent = 'ARRANGE DISHES';
        heading.setAttribute('role', 'presentation');
        heading.setAttribute('aria-hidden', 'true');
        list.append(heading);
        Array.from(select.options).forEach(option => {
            const button = document.createElement('button');
            button.type = 'button';
            button.className = 'menu-sort-option';
            button.dataset.menuSortValue = option.value;
            button.setAttribute('role', 'option');
            button.setAttribute('aria-selected', String(option.selected));
            button.tabIndex = -1;
            const copy = document.createElement('span');
            copy.textContent = cleanLabel(option);
            if (option.value === 'menu') {
                const help = document.createElement('small');
                help.textContent = 'Follow our categories';
                copy.append(help);
            }
            const check = document.createElement('span');
            check.className = 'menu-sort-option__check';
            check.textContent = '✓';
            check.setAttribute('aria-hidden', 'true');
            button.append(copy, check);
            list.append(button);
        });
        picker.append(trigger, list);
        wrapper.append(picker);
        select.hidden = true; // Still submitted in FormData; native fallback remains without JS.
    }

    function fitCategoryBook() {
        const book = browser()?.querySelector('.menu-book');
        if (book) book.dataset.canStick = String(book.offsetHeight < window.innerHeight - 112);
    }

    function prepareSearchHint() {
        const form = browser()?.querySelector('[data-menu-form]');
        const field = form?.querySelector('#menu-search');
        const entry = form?.querySelector('.menu-search__entry');
        if (!field || !entry) return;
        let hint = entry.querySelector('[data-menu-search-hint]');
        if (!hint) {
            hint = document.createElement('p');
            hint.id = 'menu-search-hint';
            hint.className = 'menu-search__hint';
            hint.dataset.menuSearchHint = '';
            hint.textContent = 'Type a dish, category or item code.';
            hint.hidden = true;
            entry.append(hint);
        }
        const descriptions = new Set((field.getAttribute('aria-describedby') || '').split(/\s+/).filter(Boolean));
        descriptions.add(hint.id);
        field.setAttribute('aria-describedby', Array.from(descriptions).join(' '));
    }

    function remindSearch(form) {
        prepareSearchHint();
        const field = form.querySelector('#menu-search');
        const hint = form.querySelector('[data-menu-search-hint]');
        if (!field) return;
        // An empty click never navigates or lets an older request replace the reminder.
        request?.abort();
        request = null;
        browser()?.removeAttribute('aria-busy');
        if (hint) hint.hidden = false;
        field.classList.remove('is-search-prompted');
        field.focus({ preventScroll: true });
        const rect = field.getBoundingClientRect();
        const navBottom = Math.max(0, document.querySelector('.site-nav')?.getBoundingClientRect().bottom || 0);
        if (rect.top < navBottom || rect.bottom > window.innerHeight) {
            field.scrollIntoView({ block: 'center', behavior: reducedMotion() ? 'instant' : 'smooth' });
        }
        announce('');
        requestAnimationFrame(() => {
            if (!field.isConnected || field.value.trim()) return;
            field.classList.add('is-search-prompted');
            announce('Type a dish, category or item code.');
        });
    }

    function upgradePhotos(container) {
        container.querySelectorAll('[data-dish-photo]').forEach(img => {
            if (img.dataset.errorBound) return;
            img.dataset.errorBound = 'true';
            const fallback = () => {
                const holder = document.createElement('div');
                holder.className = 'menu-photo-placeholder';
                const mark = document.createElement('span');
                mark.setAttribute('aria-hidden', 'true');
                mark.textContent = '夜';
                const label = document.createElement('span');
                label.textContent = 'Photo coming soon';
                holder.append(mark, label);
                img.replaceWith(holder);
            };
            img.addEventListener('error', fallback, { once: true });
            if (img.complete && img.naturalWidth === 0 && img.getAttribute('src')) fallback();
        });
    }

    function openDish(link) {
        const id = link.dataset.dishOpen;
        const template = document.getElementById(`dish-template-${id}`);
        if (!dialog || typeof dialog.showModal !== 'function' || !template || !root.contains(template)) return false;
        opener = link;
        dialog.querySelector('[data-dish-dialog-content]').replaceChildren(template.content.cloneNode(true));
        upgradePhotos(dialog);
        dialog.showModal();
        document.documentElement.classList.add('menu-detail-open');
        dialog.querySelector('button').focus({ preventScroll: true });
        return true;
    }

    async function navigate(url, { more = false, pop = false, control = null } = {}) {
        const target = new URL(url, location.href);
        if (target.origin !== location.origin || target.pathname !== location.pathname) {
            location.assign(target.href);
            return;
        }
        request?.abort();
        const currentRequest = new AbortController();
        request = currentRequest;
        const previousIds = new Set(Array.from(browser().querySelectorAll('[data-dish-id]'), card => card.dataset.dishId));
        browser().setAttribute('aria-busy', 'true');
        announce('Updating the menu.');
        try {
            const response = await fetch(target.href, {
                signal: currentRequest.signal, headers: { 'X-Requested-With': 'XMLHttpRequest' },
                credentials: 'same-origin', cache: 'no-store'
            });
            if (!response.ok || response.redirected) throw new Error('Menu request failed');
            const doc = new DOMParser().parseFromString(await response.text(), 'text/html');
            const next = doc.querySelector('[data-menu-browser]');
            if (!next) throw new Error('Menu response missing');
            if (currentRequest.signal.aborted) return;
            if (!pop) history.pushState({ menu: true }, '', target.href);
            browser().replaceWith(document.importNode(next, true));
            upgradePhotos(browser());
            fitCategoryBook();
            prepareSearchHint();
            prepareSortPicker();
            const count = browser().querySelector('.menu-results__heading p')?.textContent.trim() || 'Menu updated.';
            announce(count);
            if (more) {
                const firstNew = Array.from(browser().querySelectorAll('[data-dish-id]'))
                    .find(card => !previousIds.has(card.dataset.dishId));
                (firstNew?.querySelector('[data-dish-open]') || browser().querySelector('#menu-results-title'))?.focus({ preventScroll: true });
                firstNew?.scrollIntoView({ block: 'nearest', behavior: reducedMotion() ? 'instant' : 'smooth' });
            } else {
                const focusTarget = control ? browser().querySelector(`#${CSS.escape(control)}`) : browser().querySelector('#menu-results-title');
                focusTarget?.focus({ preventScroll: true });
                if (browser().getBoundingClientRect().top < 0 && !pop) {
                    browser().scrollIntoView({ block: 'start', behavior: reducedMotion() ? 'instant' : 'smooth' });
                }
            }
        } catch (error) {
            if (error.name !== 'AbortError' && !currentRequest.signal.aborted) {
                // Standard navigation is the fallback for connection/session failures.
                location.assign(target.href);
            }
        } finally {
            if (request === currentRequest) {
                browser()?.removeAttribute('aria-busy');
                request = null;
            }
        }
    }

    function submitMenu(form, control = null) {
        const url = new URL(form.action, location.href);
        url.search = '';
        new FormData(form).forEach((value, key) => {
            const text = String(value).trim();
            if (text && !(key === 'Sort' && text === 'menu')) url.searchParams.set(key, text);
        });
        navigate(url, { control });
    }

    root.addEventListener('click', event => {
        if (!event.target || typeof event.target.closest !== 'function' || !normalClick(event)) return;
        const sortTrigger = event.target.closest('[data-menu-sort-trigger]');
        if (sortTrigger) {
            event.preventDefault();
            if (sortTrigger.getAttribute('aria-expanded') === 'true') closeSort(true);
            else openSort(sortTrigger);
            return;
        }
        const sortOption = event.target.closest('[data-menu-sort-value]');
        if (sortOption) {
            event.preventDefault();
            const select = browser().querySelector('#menu-sort');
            const value = sortOption.dataset.menuSortValue;
            closeSort(true);
            if (select.value !== value && Array.from(select.options).some(option => option.value === value)) {
                select.value = value;
                select.dispatchEvent(new Event('change', { bubbles: true }));
            }
            return;
        }
        const detailLink = event.target.closest('[data-dish-open]');
        if (detailLink && openDish(detailLink)) { event.preventDefault(); return; }
        const link = event.target.closest('a[data-menu-link]');
        if (!link || link.hasAttribute('download') || link.target) return;
        event.preventDefault();
        navigate(link.href, { more: link.hasAttribute('data-menu-more') });
    });
    root.addEventListener('submit', event => {
        if (!event.target.matches('[data-menu-form]')) return;
        event.preventDefault();
        const field = event.target.querySelector('#menu-search');
        if (field && !field.value.trim()) { remindSearch(event.target); return; }
        field?.focus({ preventScroll: true });
        submitMenu(event.target, 'menu-search');
    });
    root.addEventListener('input', event => {
        if (event.target.id !== 'menu-search' || !event.target.value.trim()) return;
        const hint = event.target.form?.querySelector('[data-menu-search-hint]');
        if (hint) hint.hidden = true;
        event.target.classList.remove('is-search-prompted');
        if (status?.textContent === 'Type a dish, category or item code.') announce('');
    });
    root.addEventListener('change', event => {
        if (!event.target.matches('#menu-category, #menu-sort')) return;
        if (event.target.id === 'menu-sort') {
            const picker = event.target.closest('.menu-search__sort')?.querySelector('[data-menu-sort-picker]');
            const option = event.target.options[event.target.selectedIndex];
            if (picker && option) {
                const label = option.textContent.replace(/^Sort by:\s*/i, '').trim();
                picker.querySelector('.menu-sort-trigger__value').textContent = label;
                picker.querySelector('[data-menu-sort-trigger]').setAttribute('aria-label', 'Arrange dishes by: ' + label);
                picker.querySelectorAll('[data-menu-sort-value]').forEach(button => button.setAttribute('aria-selected', String(button.dataset.menuSortValue === option.value)));
            }
        }
        submitMenu(event.target.form, event.target.id === 'menu-sort' ? 'menu-sort-trigger' : event.target.id);
    });
    root.addEventListener('keydown', event => {
        const trigger = event.target.closest?.('[data-menu-sort-trigger]');
        if (trigger && ['ArrowDown', 'ArrowUp', 'Enter', ' '].includes(event.key)) {
            event.preventDefault();
            openSort(trigger, event.key === 'ArrowUp' ? 'last' : null);
            return;
        }
        const list = event.target.closest?.('[data-menu-sort-list]');
        if (!list || list.hidden) return;
        const options = Array.from(list.querySelectorAll('[data-menu-sort-value]'));
        const index = options.indexOf(document.activeElement);
        if (event.key === 'Escape') { event.preventDefault(); closeSort(true); return; }
        if (event.key === 'Tab') { closeSort(true); return; }
        if (['Enter', ' '].includes(event.key)) { event.preventDefault(); options[index]?.click(); return; }
        if (['ArrowDown', 'ArrowUp', 'Home', 'End'].includes(event.key)) {
            event.preventDefault();
            const next = event.key === 'Home' ? 0 : event.key === 'End' ? options.length - 1
                : (index + (event.key === 'ArrowDown' ? 1 : -1) + options.length) % options.length;
            options[next]?.focus({ preventScroll: true });
        } else if (event.key.length === 1 && !event.ctrlKey && !event.metaKey && !event.altKey) {
            const now = Date.now();
            sortTypeahead = now - sortTypeaheadTime > 800 ? event.key.toLowerCase() : sortTypeahead + event.key.toLowerCase();
            sortTypeaheadTime = now;
            options.find(option => option.textContent.trim().toLowerCase().startsWith(sortTypeahead))?.focus({ preventScroll: true });
        }
    });
    document.addEventListener('pointerdown', event => {
        if (!event.target.closest?.('[data-menu-sort-picker]')) closeSort();
    });
    window.addEventListener('popstate', () => navigate(location.href, { pop: true }));

    // Close restores focus to the control which opened the dialog. Escape is native.
    if (dialog && typeof dialog.showModal === 'function') {
        dialog.addEventListener('close', () => {
            document.documentElement.classList.remove('menu-detail-open');
            if (opener?.isConnected) opener.focus({ preventScroll: true });
            opener = null;
        });
        dialog.addEventListener('click', event => {
            if (event.target !== dialog) return;
            const rect = dialog.getBoundingClientRect();
            if (event.clientX < rect.left || event.clientX > rect.right || event.clientY < rect.top || event.clientY > rect.bottom) dialog.close();
        });
        // Direct dish links have server-rendered content, and also work without JS.
        if (dialog.open) {
            dialog.close();
            dialog.showModal();
            document.documentElement.classList.add('menu-detail-open');
        }
    }
    window.addEventListener('resize', fitCategoryBook);
    fitCategoryBook();
    prepareSearchHint();
    prepareSortPicker();
    upgradePhotos(root);
})();
