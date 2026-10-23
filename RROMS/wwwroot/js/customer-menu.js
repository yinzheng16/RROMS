/* Owner: Member 3. Progressive enhancement: ordinary GET forms/links also work without JS. */
(() => {
    'use strict';
    const root = document.querySelector('[data-customer-menu]');
    if (!root || root.dataset.enhanced) return;
    root.dataset.enhanced = 'true';
    const status = root.querySelector('[data-menu-status]');
    const dialog = root.querySelector('[data-dish-dialog]');
    const filterShortcut = root.querySelector('[data-menu-back-filters]');
    let filterShortcutFrame = null;
    let request = null;
    let opener = null;
    let live = null;
    let reloadSearchEdited = false;
    const browser = () => root.querySelector('[data-menu-browser]');
    const reducedMotion = () => window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    const announce = message => { if (status) { status.textContent = message; } };
    const normalClick = event => event.button === 0 && !event.ctrlKey && !event.metaKey && !event.shiftKey && !event.altKey;
    let sortTypeahead = '';
    let sortTypeaheadTime = 0;

    const categoryDialog = root.querySelector('[data-menu-category-dialog]');
    let categoriesCollapsed = false;
    let categoryCloseTimer = null;
    let categoryReturnFocus = true;
    const smallCategoryScreen = () => window.matchMedia('(max-width: 61.999rem)').matches;
    const categorySlot = () => browser()?.querySelector('[data-menu-category-slot]');
    const categoryToggle = () => browser()?.querySelector('[data-menu-categories-toggle]');

    function restoreCategoryPanel() {
        const panel = categoryDialog?.querySelector('[data-menu-category-panel]');
        const slot = categorySlot();
        if (panel && slot) slot.append(panel);
    }

    function finishCategoryClose() {
        const wasOpen = root.dataset.categoryDrawerOpen === 'true';
        window.clearTimeout(categoryCloseTimer);
        categoryCloseTimer = null;
        restoreCategoryPanel();
        categoryDialog?.classList.remove('is-closing');
        root.dataset.categoryDrawerOpen = 'false';
        document.documentElement.classList.remove('menu-category-open');
        if (categoryDialog?.open) categoryDialog.close();
        syncCategoryNavigation();
        updateFilterShortcut();
        if (wasOpen && categoryReturnFocus) categoryToggle()?.focus({ preventScroll: true });
        if (wasOpen && categoryReturnFocus) live?.notify();
    }

    function closeCategoryDrawer({ restoreFocus = true, animate = true } = {}) {
        if (!categoryDialog?.open) return;
        categoryReturnFocus = restoreFocus;
        if (categoryCloseTimer !== null) {
            if (!animate) finishCategoryClose();
            return;
        }
        if (!animate || reducedMotion()) { finishCategoryClose(); return; }
        categoryDialog.classList.add('is-closing');
        categoryCloseTimer = window.setTimeout(finishCategoryClose, 180);
    }

    function syncCategoryNavigation() {
        const slot = categorySlot();
        if (!slot) return;
        const small = smallCategoryScreen();
        // A viewport resize must never leave a modal open over the desktop layout.
        if (!small && categoryDialog?.open) {
            closeCategoryDrawer({ restoreFocus: false, animate: false });
            const target = categoriesCollapsed ? categoryToggle() : root.querySelector('[data-menu-category-close]');
            target?.focus({ preventScroll: true });
        }
        root.dataset.categoriesEnhanced = 'true';
        root.dataset.categoriesCollapsed = String(categoriesCollapsed);
        const supported = typeof categoryDialog?.showModal === 'function';
        root.dataset.categoryDrawerSupported = String(supported);
        const toolbar = browser()?.querySelector('[data-menu-category-toolbar]');
        if (toolbar) toolbar.hidden = small ? !supported : !categoriesCollapsed;
        const expanded = small ? Boolean(categoryDialog?.open) : !categoriesCollapsed;
        const toggle = categoryToggle();
        if (toggle) {
            toggle.setAttribute('aria-expanded', String(expanded));
            const label = toggle.querySelector('span');
            if (label) label.textContent = 'Categories';
            toggle.setAttribute('aria-controls', small ? 'menu-category-drawer' : 'menu-category-panel');
            if (small) toggle.setAttribute('aria-haspopup', 'dialog');
            else toggle.removeAttribute('aria-haspopup');
        }
        const panel = root.querySelector('[data-menu-category-panel]');
        if (panel) {
            panel.inert = !small && categoriesCollapsed;
            if (panel.inert) panel.setAttribute('aria-hidden', 'true');
            else panel.removeAttribute('aria-hidden');
        }
        const close = panel?.querySelector('[data-menu-category-close]');
        if (close) {
            const label = small ? 'Close categories' : 'Collapse categories';
            close.hidden = false;
            close.setAttribute('aria-label', label);
            close.title = label;
        }
    }

    function openCategoryDrawer() {
        if (!smallCategoryScreen()) {
            changeCategoryLayout(false);
            requestAnimationFrame(() => root.querySelector('[data-menu-category-close]')?.focus({ preventScroll: true }));
            return;
        }
        const panel = categorySlot()?.querySelector('[data-menu-category-panel]');
        if (!panel || !categoryDialog || typeof categoryDialog.showModal !== 'function' || categoryDialog.open) return;
        closeSort();
        categoryReturnFocus = true;
        categoryDialog.classList.remove('is-closing');
        categoryDialog.append(panel);
        root.dataset.categoryDrawerOpen = 'true';
        document.documentElement.classList.add('menu-category-open');
        categoryDialog.showModal();
        syncCategoryNavigation();
        panel.querySelector('[aria-current="true"]')?.focus({ preventScroll: true });
    }

    function collapseCategoryPanel() {
        if (smallCategoryScreen()) { closeCategoryDrawer(); return; }
        changeCategoryLayout(true);
        categoryToggle()?.focus({ preventScroll: true });
    }

    if (categoryDialog) {
        categoryDialog.addEventListener('cancel', event => {
            event.preventDefault();
            closeCategoryDrawer();
        });
        categoryDialog.addEventListener('close', finishCategoryClose);
        categoryDialog.addEventListener('click', event => {
            if (event.target !== categoryDialog) return;
            const rect = categoryDialog.getBoundingClientRect();
            if (event.clientX < rect.left || event.clientX > rect.right
                || event.clientY < rect.top || event.clientY > rect.bottom) closeCategoryDrawer();
        });
        window.addEventListener('pagehide', () => closeCategoryDrawer({ restoreFocus: false, animate: false }));
    }


    // FLIP animation: calculate the final grid once, then animate visual positions.
    // This avoids repeatedly laying out 80 photos while a sidebar width animates.
    let categoryAnimations = [];
    let categoryGhost = null;
    function changeCategoryLayout(collapsed) {
        if (categoriesCollapsed === collapsed) return;
        const scope = browser();
        const canAnimate = !reducedMotion() && typeof scope?.animate === 'function';
        const targets = canAnimate ? Array.from(scope.querySelectorAll(
            '.menu-explorer__heading, .menu-category-toolbar, [data-menu-form], .menu-filter-bar, .menu-new-arrivals__heading, [data-dish-id], .menu-results__heading, .menu-load-more'))
            .map(node => ({ node, before: node.getBoundingClientRect() }))
            .filter(({ before }) => before.width > 0 && before.bottom > 0
                && before.top < window.innerHeight && before.right > 0 && before.left < window.innerWidth) : [];
        categoryAnimations.forEach(animation => animation.cancel());
        categoryAnimations = [];
        categoryGhost?.remove();
        categoryGhost = null;
        if (canAnimate && collapsed) {
            const panel = scope.querySelector('[data-menu-category-panel]');
            const rect = panel?.getBoundingClientRect();
            if (rect && rect.width > 0 && rect.bottom > 0 && rect.top < window.innerHeight) {
                categoryGhost = panel.cloneNode(true);
                categoryGhost.removeAttribute('id');
                categoryGhost.removeAttribute('data-menu-category-panel');
                categoryGhost.querySelectorAll('[id]').forEach(node => node.removeAttribute('id'));
                categoryGhost.setAttribute('aria-hidden', 'true');
                categoryGhost.inert = true;
                categoryGhost.classList.add('menu-category-ghost');
                Object.assign(categoryGhost.style, { left: `${rect.left}px`, top: `${rect.top}px`, width: `${rect.width}px` });
                root.append(categoryGhost);
            }
        }
        categoriesCollapsed = collapsed;
        syncCategoryNavigation();
        fitCategoryBook();
        if (canAnimate) {
            const timing = { duration: 300, easing: 'cubic-bezier(.22, 1, .36, 1)' };
            for (const { node, before } of targets) {
                const after = node.getBoundingClientRect();
                if (!after.width || !after.height || !before.height) continue;
                const x = before.left - after.left, y = before.top - after.top;
                const sx = before.width / after.width, sy = before.height / after.height;
                if (Math.abs(x) + Math.abs(y) < .5 && Math.abs(sx - 1) + Math.abs(sy - 1) < .01) continue;
                categoryAnimations.push(node.animate([
                    { transformOrigin: 'top left', transform: `translate(${x}px, ${y}px) scale(${sx}, ${sy})` },
                    { transformOrigin: 'top left', transform: 'none' }
                ], timing));
            }
            const panel = categoryGhost || scope.querySelector('[data-menu-category-panel]');
            if (panel) {
                const ghost = categoryGhost;
                const animation = panel.animate(collapsed
                    ? [{ opacity: 1, transform: 'translateX(0)' }, { opacity: 0, transform: 'translateX(-1rem)' }]
                    : [{ opacity: 0, transform: 'translateX(-1rem)' }, { opacity: 1, transform: 'translateX(0)' }], timing);
                categoryAnimations.push(animation);
                animation.finished.then(() => {
                    ghost?.remove();
                    if (categoryGhost === ghost) categoryGhost = null;
                }).catch(() => ghost?.remove());
            }
        }
        // Geometry is final even during the visual animation. Update the counter afterwards.
        window.setTimeout(() => window.dispatchEvent(new Event('resize')), canAnimate ? 320 : 0);
    }

    let recentCleanup = null;

    function recentPosition() {
        const track = browser()?.querySelector('[data-menu-recent-grid]');
        if (!track) return null;
        const edge = track.getBoundingClientRect().left;
        const first = Array.from(track.children).find(card => card.getBoundingClientRect().right > edge + 2);
        return { id: first?.dataset.dishId, left: track.scrollLeft };
    }

    function prepareRecentCarousel(position = null) {
        recentCleanup?.();
        recentCleanup = null;
        const section = browser()?.querySelector('[data-menu-new-arrivals]');
        const track = section?.querySelector('[data-menu-recent-grid]');
        if (!track) return;
        const cards = Array.from(track.children).filter(card => card.matches('[data-dish-id]'));
        const controls = section.querySelector('[data-menu-recent-controls]');
        const previous = section.querySelector('[data-menu-recent-previous]');
        const next = section.querySelector('[data-menu-recent-next]');
        const counter = section.querySelector('[data-menu-recent-count]');
        if (!cards.length || !controls || !previous || !next || !counter) return;
        controls.hidden = false;
        let frame = null, animationFrame = null, destination = null;

        function geometry() {
            const first = cards[0].getBoundingClientRect();
            const step = cards.length > 1
                ? cards[1].getBoundingClientRect().left - first.left
                : first.width + (Number.parseFloat(getComputedStyle(track).columnGap) || 0);
            const gap = Math.max(0, step - first.width);
            const size = Math.min(cards.length, Math.max(1,
                Math.round((track.clientWidth + gap) / Math.max(1, step))));
            const start = Math.min(Math.max(0, cards.length - size), Math.max(0,
                Math.round((track.getBoundingClientRect().left - first.left) / Math.max(1, step))));
            return { size, start };
        }

        function warmImages(start, size) {
            // Only decode the current and next group, not the entire catalogue.
            cards.slice(start, start + size * 2).forEach(card => {
                const img = card.querySelector('img');
                if (!img || img.dataset.menuWarmed) return;
                img.dataset.menuWarmed = 'true';
                img.loading = 'eager';
                if (typeof img.decode === 'function') img.decode().catch(() => { });
            });
        }

        function update() {
            if (animationFrame !== null) return;
            const { start, size } = geometry();
            const last = Math.min(cards.length, start + size);
            const text = start + 1 === last ? `${last} of ${cards.length}` : `${start + 1}–${last} of ${cards.length}`;
            if (counter.textContent !== text) counter.textContent = text;
            previous.disabled = track.scrollLeft <= 2;
            next.disabled = track.scrollLeft + track.clientWidth >= track.scrollWidth - 2;
            warmImages(start, size);
        }

        function schedule() {
            if (animationFrame !== null || frame !== null) return;
            frame = requestAnimationFrame(() => { frame = null; update(); });
        }

        function stopAnimation() {
            if (animationFrame !== null) cancelAnimationFrame(animationFrame);
            animationFrame = destination = null;
            track.classList.remove('is-carousel-moving');
        }

        function moveTo(index) {
            const { size } = geometry();
            const target = Math.min(Math.max(0, index), Math.max(0, cards.length - size));
            const end = Math.max(0, Math.min(track.scrollWidth - track.clientWidth,
                cards[target].getBoundingClientRect().left - track.getBoundingClientRect().left + track.scrollLeft));
            stopAnimation();
            warmImages(target, size);
            if (reducedMotion() || Math.abs(end - track.scrollLeft) < 1) {
                track.scrollTo({ left: end, behavior: 'instant' });
                update();
                return;
            }
            destination = target;
            const start = track.scrollLeft;
            let started = null;
            track.classList.add('is-carousel-moving');
            function tick(time) {
                if (started === null) started = time;
                const progress = Math.min(1, Math.max(0, (time - started) / 520));
                const eased = (1 - Math.cos(Math.PI * progress)) / 2;
                track.scrollTo({ left: start + (end - start) * eased, behavior: 'instant' });
                if (progress < 1) animationFrame = requestAnimationFrame(tick);
                else {
                    animationFrame = destination = null;
                    track.classList.remove('is-carousel-moving');
                    update();
                }
            }
            animationFrame = requestAnimationFrame(tick);
        }

        function move(direction) {
            const { size, start } = geometry();
            moveTo((destination ?? start) + direction * size);
        }
        function goPrevious() { move(-1); }
        function goNext() { move(1); }
        function onKey(event) {
            if (event.target !== track || !['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) return;
            event.preventDefault();
            if (event.key === 'ArrowLeft') move(-1);
            else if (event.key === 'ArrowRight') move(1);
            else moveTo(event.key === 'Home' ? 0 : cards.length);
        }
        function onGesture() { stopAnimation(); schedule(); }
        function onResize() { stopAnimation(); schedule(); }

        previous.addEventListener('click', goPrevious);
        next.addEventListener('click', goNext);
        track.addEventListener('scroll', schedule, { passive: true });
        track.addEventListener('keydown', onKey);
        track.addEventListener('pointerdown', onGesture, { passive: true });
        track.addEventListener('wheel', onGesture, { passive: true });
        const observer = typeof ResizeObserver === 'function' ? new ResizeObserver(onResize) : null;
        observer?.observe(track);
        window.addEventListener('resize', onResize, { passive: true });
        recentCleanup = () => {
            stopAnimation();
            previous.removeEventListener('click', goPrevious);
            next.removeEventListener('click', goNext);
            track.removeEventListener('scroll', schedule);
            track.removeEventListener('keydown', onKey);
            track.removeEventListener('pointerdown', onGesture);
            track.removeEventListener('wheel', onGesture);
            observer?.disconnect();
            window.removeEventListener('resize', onResize);
            if (frame !== null) cancelAnimationFrame(frame);
        };
        if (position) {
            const card = cards.find(item => item.dataset.dishId === position.id);
            const left = card ? card.getBoundingClientRect().left - track.getBoundingClientRect().left + track.scrollLeft : position.left;
            track.scrollTo({ left, behavior: 'instant' });
        }
        update();
    }


    function clearReloadSearchDraft() {
        const navigation = window.performance?.getEntriesByType?.('navigation')?.[0];
        if (navigation?.type !== 'reload' || reloadSearchEdited) return;
        const field = browser()?.querySelector('#menu-search');
        if (field) field.value = '';
    }

    function updateFilterShortcut() {
        if (!filterShortcut) return;
        const form = browser()?.querySelector('[data-menu-form]');
        const threshold = Math.min(window.innerHeight * .5, 240);
        filterShortcut.hidden = !form || Boolean(dialog?.open) || Boolean(categoryDialog?.open)
            || form.getBoundingClientRect().bottom >= -threshold;
    }

    function scheduleFilterShortcut() {
        if (!filterShortcut || filterShortcutFrame !== null) return;
        filterShortcutFrame = requestAnimationFrame(() => {
            filterShortcutFrame = null;
            updateFilterShortcut();
        });
    }

    function returnToFilters() {
        const form = browser()?.querySelector('[data-menu-form]');
        if (!form) return;
        closeSort();
        // Returning only scrolls/focuses; it never resets filters or reloads cards.
        form.querySelector('#menu-search')?.focus({ preventScroll: true });
        // Anchor to the heading, not the form: the category toolbar can change
        // the distance between them. Keep the same comfortable gap in both layouts.
        const heading = browser()?.querySelector('.menu-explorer__heading') || form;
        const gap = Number.parseFloat(getComputedStyle(root).fontSize) || 16;
        const scrollPadding = Number.parseFloat(getComputedStyle(document.documentElement).scrollPaddingTop) || 0;
        const top = Math.max(0, window.scrollY + heading.getBoundingClientRect().top - Math.max(gap, scrollPadding));
        window.scrollTo({ top, left: window.scrollX, behavior: reducedMotion() ? 'instant' : 'smooth' });
    }

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
        if (book) {
            const canStick = String(book.offsetHeight < window.innerHeight - 112);
            book.dataset.canStick = canStick;
            if (categorySlot()) categorySlot().dataset.canStick = canStick;
        }
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
        dialog.dataset.menuDishId = id;
        dialog.querySelector('[data-dish-dialog-content]').replaceChildren(template.content.cloneNode(true));
        upgradePhotos(dialog);
        dialog.showModal();
        updateFilterShortcut();
        document.documentElement.classList.add('menu-detail-open');
        dialog.querySelector('button').focus({ preventScroll: true });
        return true;
    }

    // Keep decoded photos and unchanged cards when an accepted live update lands.
    // This runs synchronously immediately before the browser section is swapped.
    function preserveRefreshCards(previous, replacement) {
        function comparable(card) {
            const copy = card.cloneNode(true);
            copy.querySelectorAll('img[data-dish-photo]').forEach(img => {
                // Photo binding and carousel prewarming are client-only state.
                img.removeAttribute('data-error-bound');
                img.removeAttribute('data-menu-warmed');
                img.removeAttribute('loading');
            });
            return copy;
        }

        // A dish can occur in both sections. Match it within its own section.
        for (const selector of ['[data-menu-recent-grid]', '[data-dish-grid]']) {
            const oldSection = previous.querySelector(selector);
            const nextSection = replacement.querySelector(selector);
            if (!oldSection || !nextSection) continue;
            const existing = new Map(Array.from(oldSection.children)
                .filter(card => card.matches('[data-dish-id]'))
                .map(card => [card.dataset.dishId, card]));

            for (const nextCard of Array.from(nextSection.children)) {
                const oldCard = existing.get(nextCard.dataset.dishId);
                if (!oldCard) continue;
                if (comparable(oldCard).isEqualNode(comparable(nextCard))) {
                    nextCard.replaceWith(oldCard);
                    continue;
                }

                // Changed availability/text still keeps the same decoded image.
                // A genuinely different photo uses the new server-rendered image.
                const oldPhoto = oldCard.querySelector('img[data-dish-photo]');
                const nextPhoto = nextCard.querySelector('img[data-dish-photo]');
                if (!oldPhoto || !nextPhoto) continue;
                if (['src', 'srcset', 'sizes'].some(name =>
                    oldPhoto.getAttribute(name) !== nextPhoto.getAttribute(name))) continue;
                for (const name of ['alt', 'width', 'height', 'class']) {
                    const value = nextPhoto.getAttribute(name);
                    if (oldPhoto.getAttribute(name) === value) continue;
                    if (value === null) oldPhoto.removeAttribute(name);
                    else oldPhoto.setAttribute(name, value);
                }
                nextPhoto.replaceWith(oldPhoto);
            }
        }
    }


    async function navigate(url, { more = false, pop = false, control = null, refresh = false } = {}) {
        const target = new URL(url, location.href);
        if (target.origin !== location.origin || target.pathname !== location.pathname) {
            location.assign(target.href);
            return;
        }
        closeCategoryDrawer({ restoreFocus: false, animate: false });
        const savedRecent = more || refresh ? recentPosition() : null;
        const savedScroll = window.scrollY;
        const savedHorizontal = window.scrollX;
        const savedFormTop = browser()?.querySelector('[data-menu-form]')?.getBoundingClientRect().top;
        const savedDraft = browser()?.querySelector('#menu-search')?.value;
        live?.pause();
        request?.abort();
        const currentRequest = new AbortController();
        request = currentRequest;
        const previousIds = new Set(Array.from(browser().querySelectorAll('[data-dish-grid] [data-dish-id]'), card => card.dataset.dishId));
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
            if (!pop && !refresh) history.pushState({ menu: true }, '', target.href);
            const anchorBeforeSwap = browser()?.querySelector('[data-menu-form]')?.getBoundingClientRect().top;
            const replacement = document.importNode(next, true);
            if (refresh) preserveRefreshCards(browser(), replacement);
            browser().replaceWith(replacement);
            upgradePhotos(browser());
            syncCategoryNavigation();
            fitCategoryBook();
            prepareSearchHint();
            prepareSortPicker();
            prepareRecentCarousel(savedRecent);
            updateFilterShortcut();
            live?.reset();
            const count = browser().querySelector('.menu-results__heading p')?.textContent.trim() || 'Menu updated.';
            announce(count);
            if (refresh) {
                const field = browser().querySelector('#menu-search');
                if (field && savedDraft !== undefined) field.value = savedDraft;
                window.scrollTo({ top: savedScroll, behavior: 'instant' });
                field?.focus({ preventScroll: true });
            } else if (more) {
                const firstNew = Array.from(browser().querySelectorAll('[data-dish-grid] [data-dish-id]'))
                    .find(card => !previousIds.has(card.dataset.dishId));
                (firstNew?.querySelector('[data-dish-open]') || browser().querySelector('#menu-results-title'))?.focus({ preventScroll: true });
                firstNew?.scrollIntoView({ block: 'nearest', behavior: reducedMotion() ? 'instant' : 'smooth' });
            } else {
                // A completed search focuses its results, not the editing field.
                // Keep the caret available for explicit input/reminder actions only.
                const focusTarget = control && control !== 'menu-search'
                    ? browser().querySelector(`#${CSS.escape(control)}`)
                    : browser().querySelector('#menu-results-title');
                focusTarget?.focus({ preventScroll: true });
                if (control) {
                    // Keep search/sort in the same viewport position after replacing results.
                    // Do not start another smooth page scroll after submitting the form.
                    const formTop = browser()?.querySelector('[data-menu-form]')?.getBoundingClientRect().top;
                    const anchorTop = Number.isFinite(anchorBeforeSwap) ? anchorBeforeSwap : savedFormTop;
                    const correction = Number.isFinite(anchorTop) && Number.isFinite(formTop) ? formTop - anchorTop : 0;
                    window.scrollTo({ top: Math.max(0, window.scrollY + correction), left: savedHorizontal, behavior: 'instant' });
                } else if (browser().getBoundingClientRect().top < 0 && !pop) {
                    browser().scrollIntoView({ block: 'start', behavior: reducedMotion() ? 'instant' : 'smooth' });
                }
            }
            return true;
        } catch (error) {
            if (error.name !== 'AbortError' && !currentRequest.signal.aborted) {
                // Standard navigation is the fallback for connection/session failures.
                if (!refresh) location.assign(target.href);
            }
            return false;
        } finally {
            if (request === currentRequest) {
                browser()?.removeAttribute('aria-busy');
                request = null;
                live?.resume();
            }
        }
    }

    function setupLiveMenu() {
        const interval = 10000;
        let applied = null, latest = null, timer = null, polling = null;
        let generation = 0, dismissed = '', notified = '', notice = null;
        let paused = false, updating = false;
        const mapItems = state => new Map(state.items.map(item => [item.id, item]));
        const valid = value => value && typeof value.revision === 'string'
            && Array.isArray(value.items) && Array.isArray(value.matchingIds)
            && value.items.every(item => Number.isInteger(item.id) && Number.isInteger(item.categoryId)
                && typeof item.name === 'string' && typeof item.stamp === 'string'
                && (item.isAvailable === undefined || typeof item.isAvailable === 'boolean'))
            && value.matchingIds.every(Number.isInteger);

        function closeNotice(restore = true) {
            if (!notice) return;
            const ownsFocus = notice.wrapper.contains(document.activeElement);
            window.clearTimeout(notice.timer);
            notice.wrapper.remove();
            notice = null;
            if (restore && ownsFocus) browser()?.querySelector('#menu-search')?.focus({ preventScroll: true });
        }

        function sharedNotice(message, { success = false, action = false, revision = '' } = {}) {
            // Use the exact shared classes/markup already used by admin AJAX notices.
            // Do not remove notices created by the layout or another module.
            if (document.querySelector('.yoru-toast-wrap:not([data-menu-live-notice])')) return false;
            closeNotice(false);
            const wrapper = document.createElement('div');
            wrapper.className = 'yoru-toast-wrap';
            wrapper.dataset.menuLiveNotice = '';
            const toast = document.createElement('div');
            toast.className = 'yoru-toast yoru-toast--' + (success ? 'success' : 'info');
            toast.setAttribute('role', 'status');
            toast.setAttribute('aria-live', 'polite');
            toast.setAttribute('aria-atomic', 'true');
            toast.innerHTML = '<span class="yoru-toast-seal" aria-hidden="true"><i class="bi ' + (success ? 'bi-check-lg' : 'bi-info-lg') + '"></i></span>'
                + '<div class="yoru-toast-body"><p class="yoru-toast-title"><span class="yoru-toast-jp"></span></p><p class="yoru-toast-msg"></p></div>'
                + '<button type="button" class="yoru-toast-close" aria-label="Close notification"><i class="bi bi-x-lg"></i></button>';
            toast.querySelector('.yoru-toast-jp').textContent = success ? '完了' : 'お知らせ';
            toast.querySelector('.yoru-toast-title').append(document.createTextNode(success ? 'All set' : 'Menu update'));
            toast.querySelector('.yoru-toast-msg').textContent = message;
            if (action) {
                const actions = document.createElement('div');
                actions.className = 'menu-live-notice__actions';
                const button = document.createElement('button');
                button.type = 'button';
                button.className = 'btn btn-sm btn-outline-light rounded-pill';
                button.dataset.menuLiveUpdate = '';
                button.textContent = 'Update menu ↻';
                button.addEventListener('click', refreshMenu);
                actions.append(button);
                toast.querySelector('.yoru-toast-body').append(actions);
            } else {
                const bar = document.createElement('span');
                bar.className = 'yoru-toast-bar';
                bar.setAttribute('aria-hidden', 'true');
                toast.append(bar);
                bar.addEventListener('animationend', event => {
                    if (event.animationName === 'yoru-toast-time') closeNotice();
                });
            }
            toast.querySelector('.yoru-toast-close').addEventListener('click', () => {
                dismissed = revision;
                closeNotice();
            });
            toast.addEventListener('keydown', event => {
                if (event.key === 'Escape') {
                    event.preventDefault();
                    dismissed = revision;
                    closeNotice();
                }
            });
            wrapper.append(toast);
            document.body.append(wrapper);
            notice = { wrapper, timer: null };
            // Persistent action notices have no disappearing time bar. Shared success
            // notices use its CSS timer; reduced-motion users get a timed fallback.
            if (!action && reducedMotion()) notice.timer = window.setTimeout(() => closeNotice(), 5000);
            return true;
        }

        function notify() {
            if (!applied || !latest || updating) return;
            if (latest.revision === applied.revision) {
                if (notified) closeNotice();
                notified = dismissed = '';
                return;
            }
            if (dialog?.open || categoryDialog?.open || dismissed === latest.revision || notified === latest.revision) return;
            const current = mapItems(latest), before = mapItems(applied);
            const relevant = new Set(latest.matchingIds);
            const added = latest.items.filter(item => !before.has(item.id) && relevant.has(item.id));
            const removed = applied.items.filter(item => !current.has(item.id) && applied.matchingIds.includes(item.id));
            const changed = latest.items.some(item => before.has(item.id) && relevant.has(item.id) && before.get(item.id).stamp !== item.stamp);
            const reordered = latest.revision !== applied.revision && added.length === 0 && removed.length === 0;
            if (!added.length && !removed.length && !changed && !reordered) return;
            const unavailable = latest.items.filter(item => item.isAvailable === false
                && before.has(item.id) && before.get(item.id).isAvailable !== false
                && relevant.has(item.id));
            const restored = latest.items.filter(item => item.isAvailable !== false
                && before.get(item.id)?.isAvailable === false && relevant.has(item.id));
            const parts = [];
            const availabilityChanges = unavailable.length + restored.length;
            if (availabilityChanges) parts.push(`Availability changed for ${availabilityChanges} ${availabilityChanges === 1 ? 'dish' : 'dishes'}.`);
            if (removed.length) parts.push(`${removed.length} ${removed.length === 1 ? 'dish was' : 'dishes were'} removed.`);
            if (added.length) parts.push(`${added.length} new ${added.length === 1 ? 'dish is' : 'dishes are'} ready to view.`);
            if (!parts.length) parts.push('Menu details have changed.');
            parts.push('Select Update menu to apply the latest changes.');
            if (sharedNotice(parts.join(' '), { action: true, revision: latest.revision })) notified = latest.revision;
        }

        function schedule(delay = interval) {
            window.clearTimeout(timer);
            if (applied && !paused && !document.hidden) timer = window.setTimeout(poll, delay);
        }

        async function poll() {
            if (!applied || paused || document.hidden || polling) return;
            const mine = generation;
            const controller = new AbortController();
            polling = controller;
            const timeout = window.setTimeout(() => controller.abort(), 8000);
            try {
                const url = new URL(location.href);
                url.hash = '';
                url.searchParams.set('handler', 'Live');
                url.searchParams.delete('ItemId');
                const response = await fetch(url.href, {
                    signal: controller.signal,
                    headers: { Accept: 'application/json' }, credentials: 'same-origin', cache: 'no-store'
                });
                if (!response.ok || response.redirected) return;
                const next = await response.json();
                if (mine !== generation || controller.signal.aborted || !valid(next)) return;
                // Polling records pending changes only. The displayed snapshot is
                // replaced after the customer chooses Update menu or navigates.
                latest = next;
                notify();
            } catch (error) {
                // A failed background check never reloads the page or spams notifications.
            } finally {
                window.clearTimeout(timeout);
                if (polling === controller) {
                    polling = null;
                    schedule();
                }
            }
        }

        async function refreshMenu() {
            if (updating) return;
            updating = true;
            const button = notice?.wrapper.querySelector('[data-menu-live-update]');
            if (button) { button.disabled = true; button.textContent = 'Updating…'; }
            const ok = await navigate(location.href, { refresh: true });
            updating = false;
            if (ok) sharedNotice('The menu is up to date.', { success: true });
            else {
                if (button?.isConnected) { button.disabled = false; button.textContent = 'Update menu ↻'; }
                const message = notice?.wrapper.querySelector('.yoru-toast-msg');
                if (message) message.textContent = 'The menu could not be updated. Please try again.';
                announce('The menu could not be updated. Please try again.');
            }
        }

        function pause() {
            paused = true;
            generation++;
            window.clearTimeout(timer);
            polling?.abort();
            polling = null;
        }

        function reset() {
            pause();
            closeNotice(false);
            try {
                const value = JSON.parse(browser()?.querySelector('[data-menu-live-state]')?.textContent || 'null');
                applied = valid(value) ? value : null;
            } catch { applied = null; }
            latest = applied;
            notified = dismissed = '';
        }

        function resume() { paused = false; schedule(); }
        document.addEventListener('visibilitychange', () => {
            if (document.hidden) pause();
            else { paused = false; schedule(0); }
        });
        window.addEventListener('pagehide', () => { pause(); closeNotice(false); });
        window.addEventListener('pageshow', () => { paused = false; schedule(0); });
        return { reset: () => { reset(); resume(); }, pause, resume, notify };
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
        if (event.target.closest('[data-menu-categories-toggle]')) {
            event.preventDefault();
            if (!smallCategoryScreen() && !categoriesCollapsed) collapseCategoryPanel();
            else openCategoryDrawer();
            updateFilterShortcut();
            return;
        }
        if (event.target.closest('[data-menu-category-close]')) {
            event.preventDefault();
            collapseCategoryPanel();
            return;
        }
        if (event.target.closest('[data-menu-back-filters]')) {
            event.preventDefault();
            returnToFilters();
            return;
        }
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
        if (detailLink?.closest('[data-menu-unavailable="true"]')) {
            event.preventDefault();
            return;
        }
        if (detailLink && openDish(detailLink)) { event.preventDefault(); return; }
        const link = event.target.closest('a[data-menu-link]');
        if (!link || link.hasAttribute('download') || link.target) return;
        event.preventDefault();
        navigate(link.href, { more: link.hasAttribute('data-menu-more'), control: link.matches('.menu-reset, .menu-filter-chip') ? 'menu-search' : null });
    });
    root.addEventListener('submit', event => {
        if (!event.target.matches('[data-menu-form]')) return;
        event.preventDefault();
        const field = event.target.querySelector('#menu-search');
        if (field && !field.value.trim()) { remindSearch(event.target); return; }
        submitMenu(event.target, 'menu-search');
    });
    root.addEventListener('input', event => {
        if (event.target.id === 'menu-search') reloadSearchEdited = true;
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
            updateFilterShortcut();
            if (opener?.isConnected && !opener.closest('[data-menu-unavailable="true"]')) opener.focus({ preventScroll: true });
            else browser()?.querySelector('#menu-results-title')?.focus({ preventScroll: true });
            opener = null;
            dialog.dataset.menuDishId = '';
            live?.notify();
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
    window.addEventListener('scroll', scheduleFilterShortcut, { passive: true });
    window.addEventListener('resize', () => {
        syncCategoryNavigation();
        fitCategoryBook();
        scheduleFilterShortcut();
    });
    syncCategoryNavigation();
    clearReloadSearchDraft();
    window.addEventListener('pageshow', event => {
        if (!event.persisted) clearReloadSearchDraft();
    });
    fitCategoryBook();
    prepareSearchHint();
    prepareSortPicker();
    prepareRecentCarousel();
    upgradePhotos(root);
    updateFilterShortcut();
    live = setupLiveMenu();
    live.reset();
})();
