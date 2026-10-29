/* Owner: Member 3 / US24. Server-owned cart; no prices or orders are trusted from storage. */
(() => {
    'use strict';
    if (window.yoruCartEnhanced) return;
    window.yoruCartEnhanced = true;
    const menu = document.querySelector('[data-customer-menu]');
    const cart = document.querySelector('[data-customer-cart]');
    const login = document.querySelector('[data-cart-login-dialog]');
    const checkout = document.querySelector('[data-checkout-dialog]');
    let saving = null;
    let reviewing = false;
    let placing = false;
    let loginOpener = null;
    let clearCartToast = null;
    let channel = null;
    let reservationPickerController = null;
    const cartUrl = document.querySelector('[data-cart-navigation]')?.href
        || new URL('Customer/Cart', document.baseURI).href;

    function enhanceFloatingCart() {
        const floating = menu?.querySelector('[data-cart-floating]');
        if (!floating || menu.dataset.cartCustomer !== 'true') return;
        const navigation = document.querySelector('[data-cart-navigation]:not([data-cart-floating])');
        if (!navigation) return;
        let navigationVisible = true;
        let floatingVisible = false;
        let hideTimer = null;
        let enterFrame = null;
        floating.inert = true;
        floating.setAttribute('aria-hidden', 'true');
        const render = () => {
            // Native dialogs own focus and ordering controls while they are open.
            const visible = !navigationVisible && !document.querySelector('dialog[open]');
            if (visible === floatingVisible) return;
            floatingVisible = visible;
            window.clearTimeout(hideTimer);
            if (enterFrame !== null) cancelAnimationFrame(enterFrame);
            enterFrame = null;
            const reduceMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
            if (visible) {
                const wasHidden = floating.hidden;
                floating.hidden = false;
                floating.inert = false;
                floating.removeAttribute('aria-hidden');
                if (reduceMotion || !wasHidden) {
                    // Reverse an unfinished fade from its current visual position.
                    floating.classList.add('is-visible');
                } else {
                    // Paint the initial transparent state before starting the transition.
                    enterFrame = requestAnimationFrame(() => {
                        enterFrame = requestAnimationFrame(() => {
                            enterFrame = null;
                            if (floatingVisible) floating.classList.add('is-visible');
                        });
                    });
                }
            } else {
                floating.inert = true;
                floating.setAttribute('aria-hidden', 'true');
                floating.classList.remove('is-visible');
                if (reduceMotion) floating.hidden = true;
                else hideTimer = window.setTimeout(() => {
                    if (!floatingVisible) floating.hidden = true;
                }, 280);
            }
        };
        const measure = () => {
            const rect = navigation.getBoundingClientRect();
            navigationVisible = rect.width > 0 && rect.height > 0
                && rect.bottom > 0 && rect.top < window.innerHeight
                && rect.right > 0 && rect.left < window.innerWidth;
            render();
        };
        if ('IntersectionObserver' in window) {
            const observer = new IntersectionObserver(entries => {
                const entry = entries.find(item => item.target === navigation);
                if (!entry) return;
                navigationVisible = entry.isIntersecting;
                render();
            });
            observer.observe(navigation);
        } else {
            let queued = false;
            window.addEventListener('scroll', () => {
                if (queued) return;
                queued = true;
                requestAnimationFrame(() => { queued = false; measure(); });
            }, { passive: true });
        }
        new MutationObserver(render).observe(menu,
            { subtree: true, attributes: true, attributeFilter: ['open'] });
        window.addEventListener('resize', measure, { passive: true });
        window.addEventListener('pageshow', measure);
        measure();
    }

    function setCount(value, broadcast = true) {
        const count = Math.max(0, Number(value) || 0);
        document.querySelectorAll('[data-cart-navigation]').forEach(link => {
            const badge = link.querySelector('[data-cart-count]');
            if (badge) { badge.textContent = count > 99 ? '99+' : String(count); badge.hidden = count === 0; }
            link.setAttribute('aria-label', `My cart, ${count} ${count === 1 ? 'item' : 'items'}`);
        });
        if (broadcast) channel?.postMessage({ changed: true });
    }

    async function refreshCount() {
        if (!document.querySelector('[data-cart-navigation]')) return;
        try {
            const url = new URL(cartUrl); url.searchParams.set('handler', 'Count');
            const response = await fetch(url, { credentials: 'same-origin', cache: 'no-store', headers: { 'X-Requested-With': 'XMLHttpRequest' } });
            if (response.ok && !response.redirected && response.headers.get('content-type')?.includes('json'))
                setCount((await response.json()).count, false);
        } catch { /* Keep the last confirmed count while offline. */ }
    }

    function notice(message, kind = 'success', showCart = false) {
        clearCartToast?.();
        document.querySelector('[data-cart-toast]')?.remove();
        const addedToCart = kind === 'success' && showCart;
        const wrapper = document.createElement('div');
        wrapper.className = 'yoru-toast-wrap'; wrapper.dataset.cartToast = '';
        if (addedToCart) wrapper.dataset.cartAdded = '';
        const modal = Array.from(document.querySelectorAll('dialog[open]')).at(-1);
        const existing = modal ? null : document.querySelector('.yoru-toast-wrap');
        if (existing) wrapper.style.top = `${existing.getBoundingClientRect().bottom + 12}px`;
        const box = document.createElement('div');
        box.className = 'yoru-toast yoru-toast--' + kind;
        box.setAttribute('role', kind === 'error' ? 'alert' : 'status');
        box.setAttribute('aria-live', kind === 'error' ? 'assertive' : 'polite');
        box.setAttribute('aria-atomic', 'true');
        box.innerHTML = '<span class="yoru-toast-seal" aria-hidden="true"><i class="bi"></i></span>'
            + '<div class="yoru-toast-body"><p class="yoru-toast-title"><span class="yoru-toast-jp"></span></p><p class="yoru-toast-msg"></p></div>'
            + '<button type="button" class="yoru-toast-close" aria-label="Close notification"><i class="bi bi-x-lg"></i></button>'
            + '<span class="yoru-toast-bar" aria-hidden="true"></span>';
        box.querySelector('.yoru-toast-seal i').className += kind === 'error' ? ' bi-exclamation-lg' : ' bi-check-lg';
        box.querySelector('.yoru-toast-jp').textContent = kind === 'error' ? '注意' : '完了';
        box.querySelector('.yoru-toast-title').append(document.createTextNode(kind === 'error' ? 'Oops' : addedToCart ? 'Added to cart' : 'All set'));
        box.querySelector('.yoru-toast-msg').textContent = message;
        if (showCart) {
            const link = document.createElement('a'); link.href = cartUrl;
            link.className = 'cart-toast-action'; link.textContent = 'View cart →';
            box.querySelector('.yoru-toast-body').append(link);
        }
        let timer = null, leaveTimer = null, closing = false;
        const remove = () => {
            window.clearTimeout(timer); window.clearTimeout(leaveTimer); wrapper.remove();
        };
        clearCartToast = remove;
        const close = () => {
            if (closing) return;
            closing = true; window.clearTimeout(timer);
            box.classList.add('is-leaving');
            leaveTimer = window.setTimeout(remove,
                window.matchMedia('(prefers-reduced-motion: reduce)').matches ? 300 : 400);
        };
        box.querySelector('.yoru-toast-close').addEventListener('click', close);
        box.querySelector('.yoru-toast-bar').addEventListener('animationend', close);
        box.addEventListener('keydown', event => { if (event.key === 'Escape') close(); });
        // The team CSS owns the animated timer and pauses it on hover/focus.
        if (window.matchMedia('(prefers-reduced-motion: reduce)').matches) {
            const start = () => { if (closing) return; window.clearTimeout(timer); timer = window.setTimeout(close, addedToCart ? 4000 : 5000); };
            box.addEventListener('mouseenter', () => window.clearTimeout(timer));
            box.addEventListener('mouseleave', start);
            box.addEventListener('focusin', () => window.clearTimeout(timer));
            box.addEventListener('focusout', start);
            start();
        }
        wrapper.append(box);
        // Native dialogs occupy the browser's top layer. An error must be inside
        // that layer to remain visible above the modal backdrop.
        if (modal) { wrapper.dataset.cartInDialog = ''; modal.prepend(wrapper); }
        else document.body.append(wrapper);
    }

    function focusAfterDialog() {
        document.documentElement.classList.remove('cart-modal-open');
        (loginOpener?.isConnected ? loginOpener : cart?.querySelector('[data-cart-review]'))?.focus({ preventScroll: true });
    }

    function signInPrompt(form) {
        const id = form.querySelector('[name="menuItemId"], [name="ItemId"]')?.value;
        const destination = new URL(location.href); destination.searchParams.set('ItemId', id || ''); destination.hash = 'dish-details';
        const returnUrl = destination.pathname + destination.search + destination.hash;
        try {
            sessionStorage.setItem('yoru-dish-draft', JSON.stringify({
                id,
                quantity: form.querySelector('[name="quantity"]')?.value || '1',
                notes: form.querySelector('[name="notes"]')?.value || ''
            }));
        } catch { /* Storage is optional; authentication still works. */ }
        if (!login || typeof login.showModal !== 'function') {
            const url = new URL('/Identity/Account/Login', location.origin); url.searchParams.set('returnUrl', returnUrl); location.assign(url); return;
        }
        for (const selector of ['[data-cart-login-link]', '[data-cart-register-link]']) {
            const link = login.querySelector(selector); const url = new URL(link.href);
            url.searchParams.set('returnUrl', returnUrl); link.href = url;
        }
        loginOpener = document.activeElement;
        if (!login.open) login.showModal();
        document.documentElement.classList.add('cart-modal-open');
    }

    function restoreDishDraft() {
        if (menu?.dataset.cartCustomer !== 'true') return;
        const form = document.querySelector('[data-dish-dialog][open] [data-cart-detail-form]');
        if (!form || form.dataset.draftRestored) return;
        form.dataset.draftRestored = 'true';
        try {
            const draft = JSON.parse(sessionStorage.getItem('yoru-dish-draft') || 'null');
            if (!draft || draft.id !== form.querySelector('[name="menuItemId"]')?.value) return;
            const quantity = form.querySelector('[name="quantity"]');
            quantity.value = String(Math.max(1, Math.min(100, Number(draft.quantity) || 1)));
            const notes = form.querySelector('[name="notes"]'); notes.value = String(draft.notes || '').slice(0, 200);
            const disclosure = notes.closest('details');
            if (notes.value && disclosure) disclosure.open = true;
            syncStepper(quantity);
        } catch { /* Ignore an old or malformed draft. */ }
    }

    async function post(form, url = form.action, extras = null) {
        const data = new FormData(form);
        if (extras) for (const [key, value] of Object.entries(extras)) data.set(key, value);
        return fetch(url, { method: 'POST', body: data, credentials: 'same-origin', cache: 'no-store', headers: { 'X-Requested-With': 'XMLHttpRequest' } });
    }

    function customizeDish(form) {
        if (menu?.dataset.cartCustomer !== 'true') {
            if (menu?.dataset.cartSignedIn === 'true') notice('Only customer accounts can add dishes to a cart.', 'error');
            else signInPrompt(form);
            return;
        }
        // Reuse the menu's existing popup, focus handling and native GET fallback.
        const link = form.closest('[data-dish-id]')?.querySelector('[data-dish-open]');
        if (link?.hasAttribute('href')) link.click();
        else HTMLFormElement.prototype.submit.call(form);
    }

    async function add(form) {
        if (form.dataset.submitting || !form.reportValidity()) return;
        if (menu?.dataset.cartCustomer !== 'true') {
            if (menu?.dataset.cartSignedIn === 'true') notice('Only customer accounts can add dishes to a cart.', 'error');
            else signInPrompt(form);
            return;
        }
        form.dataset.submitting = 'true';
        const button = form.querySelector('[type="submit"]'); button.disabled = true; button.setAttribute('aria-busy', 'true');
        try {
            const response = await post(form);
            if (response.status === 401 || response.redirected) { signInPrompt(form); return; }
            if (!response.headers.get('content-type')?.includes('json')) throw new Error('Unable to add this dish. Please refresh and try again.');
            const result = await response.json();
            if (!response.ok || !result.success) { notice(result.message || 'Unable to add this dish.', 'error'); return; }
            setCount(result.count);
            try { sessionStorage.removeItem('yoru-dish-draft'); } catch { /* Optional. */ }
            const dishDialog = form.closest('[data-dish-dialog]'); if (dishDialog?.open) dishDialog.close();
            notice(result.message, 'success', true);
        } catch (error) { notice(error.message || 'Connection lost. Please try again.', 'error'); }
        finally { delete form.dataset.submitting; button.disabled = false; button.removeAttribute('aria-busy'); }
    }

    const content = () => cart?.querySelector('[data-cart-content]');
    const cartForm = () => cart?.querySelector('[data-cart-form]');
    function savedState(text) {
        const state = cart?.querySelector('[data-cart-save-state]'); if (state) state.textContent = text;
        const status = cart?.querySelector('[data-cart-status]'); if (status) status.textContent = text;
    }
    function enhanceReservationPicker() {
        reservationPickerController?.abort();
        const select = cart?.querySelector('#cart-reservation');
        if (!select) return;
        reservationPickerController = new AbortController();
        const events = { signal: reservationPickerController.signal };
        const element = (tag, className, text) => {
            const node = document.createElement(tag);
            if (className) node.className = className;
            if (text !== undefined) node.textContent = text;
            return node;
        };
        const icon = name => {
            const node = element('i', `bi bi-${name}`);
            node.setAttribute('aria-hidden', 'true');
            return node;
        };
        const sibling = select.nextElementSibling;
        const picker = sibling?.matches('.cart-reservation-picker')
            ? sibling : element('div', 'cart-reservation-picker');
        const trigger = picker.querySelector(':scope > .cart-reservation__trigger')
            || element('button', 'cart-reservation__trigger');
        picker.querySelector(':scope > .cart-reservation__popup')?.remove();
        trigger.type = 'button'; trigger.id = 'cart-reservation-trigger';
        trigger.setAttribute('role', 'combobox');
        trigger.setAttribute('aria-haspopup', 'listbox');
        trigger.setAttribute('aria-expanded', 'false');
        trigger.setAttribute('aria-controls', 'cart-reservation-options');
        trigger.setAttribute('aria-labelledby', 'cart-reservation-label cart-reservation-value');
        const value = trigger.querySelector('.cart-reservation__value')
            || element('span', 'cart-reservation__value');
        value.id = 'cart-reservation-value';
        const chevron = trigger.querySelector('.cart-reservation__chevron')
            || element('span', 'cart-reservation__chevron');
        chevron.setAttribute('aria-hidden', 'true');
        const popup = element('div', 'cart-reservation__popup'); popup.hidden = true;
        const list = element('div', 'cart-reservation__list'); list.id = 'cart-reservation-options';
        list.setAttribute('role', 'listbox'); list.setAttribute('aria-labelledby', 'cart-reservation-label');
        popup.append(list);
        const choices = Array.from(select.options);
        let active = 0, search = '', searchAt = 0;
        const rows = choices.map((option, index) => {
            if (option.dataset.bookingDate && !list.querySelector('.cart-reservation__group')) {
                const heading = element('p', 'cart-reservation__group', 'Your reservations');
                heading.setAttribute('role', 'presentation'); list.append(heading);
            }
            const row = element('div', 'cart-reservation__option');
            row.id = `cart-reservation-option-${index}`;
            row.setAttribute('role', 'option'); row.dataset.index = index;
            if (option.dataset.unavailable) row.setAttribute('aria-disabled', 'true');
            const text = element('span', 'cart-reservation__option-text');
            text.append(element('span', 'cart-reservation__option-title', option.dataset.bookingDate || option.textContent));
            if (option.dataset.bookingDetails) text.append(element('span', 'cart-reservation__option-details', option.dataset.bookingDetails));
            row.append(icon(option.dataset.unavailable ? 'exclamation-circle' : 'calendar4'), text, icon('check-lg'));
            list.append(row); return row;
        });
        if (!choices.some(option => option.dataset.bookingDate)) {
            const empty = element('div', 'cart-reservation__empty'); empty.id = 'cart-reservation-empty';
            empty.append(icon('calendar4'), element('p', '', 'No eligible reservations yet.'), element('small', '', 'Your bookings will appear here.'));
            popup.append(empty); trigger.setAttribute('aria-describedby', empty.id);
        }
        const sync = () => {
            const option = choices[select.selectedIndex] || choices[0];
            trigger.querySelector(':scope > i')?.remove();
            trigger.prepend(icon(option.dataset.unavailable ? 'exclamation-circle' : 'calendar4'));
            value.replaceChildren(element('span', '', option.dataset.bookingDate || option.textContent));
            if (option.dataset.bookingDetails) value.append(element('small', '', option.dataset.bookingDetails));
            rows.forEach((row, index) => row.setAttribute('aria-selected', String(index === select.selectedIndex)));
            trigger.disabled = select.disabled;
        };
        const close = () => {
            popup.hidden = true; picker.classList.remove('is-open');
            trigger.setAttribute('aria-expanded', 'false'); trigger.removeAttribute('aria-activedescendant');
        };
        const position = () => {
            if (popup.hidden) return;
            const rect = trigger.getBoundingClientRect();
            const below = window.innerHeight - rect.bottom - 12, above = rect.top - 12;
            const upward = below < Math.min(popup.scrollHeight, 240) && above > below;
            picker.classList.toggle('opens-up', upward);
            popup.style.maxHeight = `${Math.max(48, Math.min(352, upward ? above : below))}px`;
        };
        const activate = index => {
            active = index;
            rows.forEach((row, i) => row.classList.toggle('is-active', i === index));
            trigger.setAttribute('aria-activedescendant', rows[index].id);
            const rect = rows[index].getBoundingClientRect(), bounds = popup.getBoundingClientRect();
            if (rect.top < bounds.top) popup.scrollTop -= bounds.top - rect.top + 4;
            else if (rect.bottom > bounds.bottom) popup.scrollTop += rect.bottom - bounds.bottom + 4;
        };
        const open = () => {
            if (trigger.disabled) return;
            popup.hidden = false; picker.classList.add('is-open'); trigger.setAttribute('aria-expanded', 'true');
            position();
            activate(choices[select.selectedIndex]?.dataset.unavailable ? 0 : Math.max(0, select.selectedIndex));
        };
        const choose = index => {
            if (choices[index].dataset.unavailable) return;
            const changed = select.selectedIndex !== index;
            select.selectedIndex = index; sync(); close(); trigger.focus({ preventScroll: true });
            if (changed) select.dispatchEvent(new Event('change', { bubbles: true }));
        };
        trigger.append(value, chevron); picker.append(trigger, popup); select.after(picker);
        picker.classList.add('is-enhanced');
        select.hidden = true; select.tabIndex = -1; select.classList.add('cart-reservation__native');
        cart.querySelector('#cart-reservation-label').htmlFor = trigger.id;
        sync();
        trigger.addEventListener('click', () => popup.hidden ? open() : close(), events);
        trigger.addEventListener('keydown', event => {
            const key = event.key;
            if (key === 'Escape') { if (!popup.hidden) { event.preventDefault(); close(); } return; }
            if (key === 'Tab') { close(); return; }
            if (['ArrowDown', 'ArrowUp', 'Home', 'End', 'Enter', ' '].includes(key)) {
                event.preventDefault();
                if (popup.hidden) { open(); return; }
                if (key === 'Enter' || key === ' ') { choose(active); return; }
                const eligible = choices.map((o, i) => o.dataset.unavailable ? -1 : i).filter(i => i >= 0);
                const current = eligible.indexOf(active);
                const next = key === 'Home' ? 0 : key === 'End' ? eligible.length - 1
                    : Math.max(0, Math.min(eligible.length - 1, current + (key === 'ArrowDown' ? 1 : -1)));
                activate(eligible[next]); return;
            }
            if (key.length === 1 && !event.ctrlKey && !event.metaKey && !event.altKey) {
                event.preventDefault();
                const now = Date.now(); search = now - searchAt > 700 ? key : search + key; searchAt = now;
                if (popup.hidden) open();
                const match = choices.findIndex(o => !o.dataset.unavailable && `${o.dataset.bookingDate || o.textContent} ${o.dataset.bookingDetails || ''}`.toLowerCase().startsWith(search.toLowerCase()));
                if (match >= 0) activate(match);
            }
        }, events);
        popup.addEventListener('pointerdown', event => { if (event.target.closest('[role="option"]')) event.preventDefault(); }, events);
        popup.addEventListener('click', event => {
            const row = event.target.closest('[role="option"]'); if (row) choose(Number(row.dataset.index));
        }, events);
        select.addEventListener('change', sync, events);
        document.addEventListener('pointerdown', event => { if (!picker.contains(event.target)) close(); }, events);
        document.addEventListener('focusin', event => { if (!picker.contains(event.target)) close(); }, events);
        window.addEventListener('resize', position, { ...events, passive: true });
        window.addEventListener('scroll', event => { if (!popup.contains(event.target)) position(); }, { ...events, passive: true, capture: true });
    }
    function enhanceCart() {
        const button = cart?.querySelector('[data-cart-save-button]'); if (button) button.hidden = true;
        cartForm()?.querySelectorAll('[data-cart-quantity-input]').forEach(syncStepper);
        const value = content()?.dataset.cartQuantity; if (value !== undefined) setCount(value, false);
        savedState('All changes saved.');
        enhanceReservationPicker();
    }

    function replaceCart(html, returnFocusId = null) {
        const doc = new DOMParser().parseFromString(html, 'text/html');
        const next = doc.querySelector('[data-cart-content]'); if (!next) throw new Error('Cart response missing. Please refresh.');
        const old = content(); const scroll = { top: window.scrollY, left: window.scrollX, behavior: 'instant' };
        const dishScrollTop = old.querySelector('.cart-dish-list')?.scrollTop || 0;
        const active = document.activeElement;
        const focusId = cart?.contains(active) ? active.id : active === document.body ? returnFocusId : null;
        const focusedRow = active?.closest('[data-cart-line]')?.dataset.cartLine;
        const focusedStep = active?.dataset.cartStep;
        const selection = active && 'selectionStart' in active ? [active.selectionStart, active.selectionEnd] : null;
        old.replaceWith(document.importNode(next, true));
        enhanceCart();
        const dishList = content().querySelector('.cart-dish-list');
        if (dishList) dishList.scrollTop = dishScrollTop;
        let target = focusId ? document.getElementById(focusId) : null;
        if (!target && focusedRow && focusedStep) target = Array.from(content().querySelectorAll('[data-cart-line]'))
            .find(row => row.dataset.cartLine === focusedRow)?.querySelector(`[data-cart-step="${focusedStep}"]`);
        target?.focus({ preventScroll: true });
        if (selection && typeof target?.setSelectionRange === 'function' && target.type !== 'number')
            target.setSelectionRange(...selection);
        window.scrollTo(scroll);
    }

    async function reloadCart() {
        const response = await fetch(cartUrl, { credentials: 'same-origin', cache: 'no-store', headers: { 'X-Requested-With': 'XMLHttpRequest' } });
        if (response.redirected || response.status === 401) { location.assign(response.url || cartUrl); return; }
        if (!response.ok) throw new Error('Unable to refresh your cart. Please refresh the page.');
        replaceCart(await response.text());
    }

    function saveCart(removeId = null) {
        if (saving) return saving.then(ok => removeId && ok ? saveCart(removeId) : ok);
        const form = cartForm(); if (!form || !form.reportValidity()) return Promise.resolve(false);
        const returnFocusId = document.activeElement?.id === 'cart-reservation-trigger' ? 'cart-reservation-trigger' : null;
        saving = (async () => {
            savedState('Saving your changes…');
            const request = post(form, form.action, removeId ? { removeLineId: removeId } : null);
            form.inert = true; form.setAttribute('aria-busy', 'true');
            try {
                const response = await request;
                if (response.redirected || response.status === 401) { location.assign(response.url || cartUrl); return false; }
                if (!response.headers.get('content-type')?.includes('html')) throw new Error('Unable to save. Please refresh and try again.');
                const html = await response.text();
                const message = decodeURIComponent(response.headers.get('X-Cart-Message') || 'Unable to save your cart.');
                if (response.ok || response.status === 409) replaceCart(html, returnFocusId);
                if (!response.ok) { savedState(message); notice(message, 'error'); return false; }
                setCount(response.headers.get('X-Cart-Count'));
                if (removeId) notice(message);
                return true;
            } catch (error) {
                savedState('Changes not saved. Try Save cart again.');
                const retry = cart?.querySelector('[data-cart-save-button]'); if (retry) retry.hidden = false;
                notice(error.message || 'Connection lost. Your edits are still here.', 'error'); return false;
            } finally {
                form.inert = false; form.removeAttribute('aria-busy'); saving = null;
                if (returnFocusId && form.isConnected && document.activeElement === document.body)
                    document.getElementById(returnFocusId)?.focus({ preventScroll: true });
            }
        })();
        return saving;
    }

    async function review() {
        if (reviewing || placing) return;
        reviewing = true;
        try {
            if (saving && !await saving) return;
            const form = cartForm(); if (!form || !form.reportValidity()) return;
            form.inert = true;
            const url = new URL(form.action); url.searchParams.set('handler', 'Review');
            const response = await post(form, url);
            if (response.redirected || response.status === 401) { location.assign(response.url || cartUrl); return; }
            if (!response.ok) {
                const result = response.headers.get('content-type')?.includes('json') ? await response.json() : { message: 'Unable to review this order. Please refresh and try again.' };
                if (result.reload) await reloadCart(); notice(result.message, 'error'); return;
            }
            const html = await response.text();
            if (!checkout || typeof checkout.showModal !== 'function') { location.assign(cartUrl); return; }
            const doc = new DOMParser().parseFromString(html, 'text/html');
            checkout.querySelector('[data-checkout-content]').replaceChildren(...Array.from(doc.body.childNodes, el => document.importNode(el, true)));
            checkout.showModal(); document.documentElement.classList.add('cart-modal-open');
            checkout.querySelector('[data-checkout-close]')?.focus({ preventScroll: true });
        } catch (error) { notice(error.message || 'Unable to review your order.', 'error'); }
        finally { if (cartForm()) cartForm().inert = false; reviewing = false; }
    }

    async function place(form) {
        if (placing) return;
        const confirmButton = form.querySelector('button[type="submit"]');
        const confirmLabel = confirmButton?.textContent;
        placing = true;
        if (confirmButton) confirmButton.textContent = 'Placing order…';
        form.querySelectorAll('button').forEach(button => { button.disabled = true; });
        checkout?.setAttribute('aria-busy', 'true');
        try {
            const response = await post(form);
            if (response.redirected || response.status === 401) { location.assign(response.url || cartUrl); return; }
            if (!response.headers.get('content-type')?.includes('json')) throw new Error('We could not confirm the response. Try Confirm order again; the same confirmation will not create a second order.');
            const result = await response.json();
            if (response.ok && result.redirect) { setCount(result.count); location.assign(result.redirect); return; }
            checkout?.close();
            if (result.reload) await reloadCart();
            notice(result.message || 'Unable to place your order.', 'error');
        } catch (error) { notice(error.message || 'Connection lost. Retry the same confirmation to check your order.', 'error'); }
        finally {
            placing = false;
            if (confirmButton) confirmButton.textContent = confirmLabel;
            form.querySelectorAll('button').forEach(button => { button.disabled = false; });
            checkout?.removeAttribute('aria-busy');
        }
    }

    function syncStepper(input) {
        const box = input.closest('.cart-quantity'); if (!box) return;
        const value = Number(input.value);
        box.querySelector('[data-cart-step="-1"]').disabled = value <= 1;
        box.querySelector('[data-cart-step="1"]').disabled = value >= 100;
    }

    document.addEventListener('click', event => {
        const step = event.target.closest('[data-cart-step]');
        if (step && !step.disabled) {
            const input = step.closest('.cart-quantity').querySelector('[data-cart-quantity-input]');
            input.value = String(Math.max(1, Math.min(100, (Number(input.value) || 1) + Number(step.dataset.cartStep))));
            syncStepper(input); input.dispatchEvent(new Event('change', { bubbles: true })); return;
        }
        if (event.target.closest('[data-cart-login-close]')) { event.preventDefault(); login?.close(); return; }
        if (event.target.closest('[data-checkout-close]')) { event.preventDefault(); if (!placing) checkout?.close(); return; }
        if (event.target.closest('[data-cart-takeaway]')) {
            const take = cartForm()?.querySelector('[name="Input.OrderType"][value="Takeaway"]');
            if (take) { take.checked = true; take.dispatchEvent(new Event('change', { bubbles: true })); }
        }
    });
    document.addEventListener('submit', event => {
        if (event.target.matches('[data-cart-customize]')) { event.preventDefault(); customizeDish(event.target); }
        else if (event.target.matches('[data-cart-add]')) { event.preventDefault(); add(event.target); }
        else if (event.target.matches('[data-cart-place]')) { event.preventDefault(); place(event.target); }
        else if (event.target.matches('[data-cart-form]')) {
            event.preventDefault();
            if (event.submitter?.matches('[data-cart-review]')) review();
            else saveCart(event.submitter?.matches('[data-cart-remove]') ? event.submitter.value : null);
        }
    });
    document.addEventListener('change', event => {
        const input = event.target;
        if (input.matches('[data-cart-quantity-input]')) syncStepper(input);
        if (!input.closest('[data-cart-form]')) return;
        if (input.matches('[data-cart-order-type]')) {
            const takeaway = input.value === 'Takeaway';
            const reservation = cartForm().querySelector('[data-cart-reservation]'); reservation.hidden = takeaway;
            cartForm().querySelector('[data-cart-takeaway-message]').hidden = !takeaway;
            reservation.querySelector('select').disabled = takeaway;
            const reservationTrigger = reservation.querySelector('.cart-reservation__trigger');
            if (reservationTrigger) reservationTrigger.disabled = takeaway;
        }
        if (input.matches('[data-cart-quantity-input], [data-cart-order-type], [data-cart-autosave]'))
            window.setTimeout(() => { if (!reviewing && !placing) saveCart(); }, 0);
    });
    for (const modal of [login, checkout].filter(Boolean)) {
        modal.addEventListener('close', focusAfterDialog);
        modal.addEventListener('cancel', event => { if (placing) event.preventDefault(); });
        modal.addEventListener('click', event => {
            if (event.target !== modal || placing) return;
            const rect = modal.getBoundingClientRect();
            if (event.clientX < rect.left || event.clientX > rect.right || event.clientY < rect.top || event.clientY > rect.bottom) modal.close();
        });
    }
    const dishDialog = document.querySelector('[data-dish-dialog]');
    if (dishDialog) new MutationObserver(restoreDishDraft).observe(dishDialog, { childList: true, subtree: true, attributes: true, attributeFilter: ['open'] });
    if ('BroadcastChannel' in window) {
        channel = new BroadcastChannel('yoru-customer-cart'); channel.onmessage = () => refreshCount();
    }
    window.addEventListener('focus', refreshCount);
    document.addEventListener('visibilitychange', () => { if (!document.hidden) refreshCount(); });
    enhanceCart(); enhanceFloatingCart(); restoreDishDraft(); refreshCount();
})();
