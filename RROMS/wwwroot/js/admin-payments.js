// US40 Admin → Payments (Owner: Member 4)
// 1) Keep the scroll position when a filter, search or quick date is used
//    (the page reloads with the new filters but stays where the admin was).
// 2) Receipt button: preview the PDF in a pop-up; the admin decides to download.
// 3) US54 refunds: "⋯" menu, refund pop-up, message after a refund.
(() => {
    'use strict';

    const page = document.querySelector('.ap-page');
    if (!page) return;

    // ---------- 1) keep the scroll position ----------
    const KEY = 'apScrollY';

    const remember = () => {
        try { sessionStorage.setItem(KEY, String(window.scrollY)); } catch (e) { /* storage blocked: just scroll to top */ }
    };

    // Filter links (status chips, quick dates, Clear all) and the search form.
    page.querySelectorAll('.ap-chip, .ap-quick a, .ap-clear').forEach(a => a.addEventListener('click', remember));
    // Search form: check the dates first (the server checks again), then remember the place.
    const form = page.querySelector('.ap-filters');
    const from = document.getElementById('apFrom');
    const to = document.getElementById('apTo');
    const dateError = document.getElementById('apDateError');

    // To cannot be before From, From cannot be after To.
    const syncLimits = () => {
        if (!from || !to) return;
        to.min = from.value || '';
        from.max = to.value || to.max;
    };

    const datesValid = () => {
        const ok = !from || !to || !from.value || !to.value || from.value <= to.value;
        if (dateError) dateError.hidden = ok;
        return ok;
    };

    if (from && to) {
        from.addEventListener('change', () => { syncLimits(); datesValid(); });
        to.addEventListener('change', () => { syncLimits(); datesValid(); });
        syncLimits();
    }

    if (form) {
        form.addEventListener('submit', e => {
            if (!datesValid()) { e.preventDefault(); return; }
            remember();
        });
    }

    let saved = null;
    try {
        saved = sessionStorage.getItem(KEY);
        sessionStorage.removeItem(KEY);
    } catch (e) { /* ignore */ }

    if (saved !== null) {
        if ('scrollRestoration' in history) history.scrollRestoration = 'manual';
        const y = parseInt(saved, 10) || 0;
        // The list may now be shorter; the browser stops at the bottom by itself.
        // 'instant': jump straight there. Bootstrap turns on smooth scrolling
        // for the whole page, which would show a scrolling animation here.
        window.scrollTo({ top: y, left: 0, behavior: 'instant' });
    }
    document.documentElement.classList.remove('ap-restoring');

    // ---------- 2) receipt preview pop-up ----------
    const dialog = document.getElementById('apPdf');
    const frame = document.getElementById('apPdfFrame');
    const title = document.getElementById('apPdfTitle');
    const download = document.getElementById('apPdfDownload');
    if (!dialog || !frame || typeof dialog.showModal !== 'function') return;   // old browser: link opens a new tab

    page.querySelectorAll('[data-ap-receipt]').forEach(link => {
        link.addEventListener('click', e => {
            e.preventDefault();
            title.textContent = 'Receipt ' + link.dataset.apReceipt;
            download.href = link.dataset.apDownload;
            frame.src = link.href;
            dialog.showModal();
        });
    });

    const close = () => dialog.close();
    dialog.querySelectorAll('[data-ap-close]').forEach(b => b.addEventListener('click', close));

    // Click on the dark area outside the box closes it too.
    dialog.addEventListener('click', e => { if (e.target === dialog) close(); });

    // Empty the frame after closing, so the next receipt never shows the old one first.
    dialog.addEventListener('close', () => { frame.src = 'about:blank'; });
})();

// ---------- 3) US54 refunds (Member 4) ----------
(() => {
    'use strict';

    const page = document.querySelector('.ap-page');
    if (!page) return;

    // ----- "⋯" menu -----
    let openMenu = null;

    const closeMenu = (focusButton = false) => {
        if (!openMenu) return;
        openMenu.menu.hidden = true;
        openMenu.button.setAttribute('aria-expanded', 'false');
        if (focusButton) openMenu.button.focus();
        openMenu = null;
    };

    page.querySelectorAll('.ap-more').forEach(box => {
        const button = box.querySelector('.ap-ab-more');
        const menu = box.querySelector('.ap-menu');
        if (!button || !menu) return;

        button.addEventListener('click', e => {
            e.stopPropagation();
            if (openMenu && openMenu.menu === menu) { closeMenu(); return; }
            closeMenu();

            menu.hidden = false;
            // Under the button, right edges lined up (the table would clip an absolute menu).
            const r = button.getBoundingClientRect();
            const w = menu.offsetWidth;
            menu.style.left = Math.max(8, r.right - w) + 'px';
            const below = r.bottom + 6;
            menu.style.top = (below + menu.offsetHeight > window.innerHeight - 8
                ? r.top - 6 - menu.offsetHeight
                : below) + 'px';

            button.setAttribute('aria-expanded', 'true');
            openMenu = { button, menu };
            menu.querySelector('[role="menuitem"]')?.focus();
        });
    });

    document.addEventListener('click', e => { if (openMenu && !openMenu.menu.contains(e.target)) closeMenu(); });
    document.addEventListener('keydown', e => { if (e.key === 'Escape') closeMenu(true); });
    window.addEventListener('scroll', () => closeMenu(), { passive: true, capture: true });
    window.addEventListener('resize', () => closeMenu());

    // ----- refund pop-up -----
    const dialog = document.getElementById('apRefund');
    const form = document.getElementById('apRfForm');
    if (!dialog || !form || typeof dialog.showModal !== 'function') return;

    const $ = id => document.getElementById(id);
    const reason = $('apRfReason');
    const note = $('apRfNote');
    const noteHint = $('apRfNoteHint');
    const returned = $('apRfReturned');
    const checkRow = $('apRfCheckRow');
    const err = $('apRfErr');
    const go = $('apRfGo');
    let online = false;

    const showError = text => { err.textContent = text; err.hidden = !text; };

    const syncNote = () => {
        const other = reason.value === 'Other';
        noteHint.textContent = other ? 'required for Other' : 'optional';
        note.required = other;
    };

    const open = d => {
        closeMenu();
        form.reset();
        showError('');
        go.disabled = false;

        $('apRfId').value = d.apRefund;
        $('apRfTitle').textContent = 'Refund ' + d.no;
        $('apRfBill').textContent = d.bill;
        $('apRfCust').textContent = d.cust;
        $('apRfMethod').textContent = d.method;
        $('apRfAmount').textContent = d.amount;
        dialog.querySelectorAll('[data-ap-amount]').forEach(s => { s.textContent = d.amount; });

        online = d.online === '1';
        $('apRfHow').textContent = online
            ? 'RM ' + d.amount + ' will be returned to the customer\u2019s card through Stripe.'
            : 'Return RM ' + d.amount + ' to the customer yourself. This only records the refund.';
        checkRow.hidden = online;

        if (d.reason) reason.value = d.reason;   // "Refund this one" picks Charged twice
        syncNote();

        dialog.showModal();
        (d.reason ? note : reason).focus();
    };

    page.querySelectorAll('[data-ap-refund]').forEach(b => b.addEventListener('click', () => open(b.dataset)));

    // ----- refund details (full reason and note) -----
    const info = document.getElementById('apRefundInfo');
    if (info) {
        page.querySelectorAll('[data-ap-refund-info]').forEach(b => b.addEventListener('click', () => {
            const d = b.dataset;
            $('apRfiTitle').textContent = 'Refund ' + d.no;
            $('apRfiWhen').textContent = d.when;
            $('apRfiBy').textContent = d.by;
            $('apRfiReason').textContent = d.reason;
            const n = $('apRfiNote');
            n.textContent = d.note || 'No note was added.';
            n.classList.toggle('is-empty', !d.note);
            info.showModal();
        }));
        info.querySelectorAll('[data-ap-close]').forEach(b => b.addEventListener('click', () => info.close()));
        info.addEventListener('click', e => { if (e.target === info) info.close(); });
    }

    reason.addEventListener('change', () => { syncNote(); showError(''); });
    note.addEventListener('input', () => showError(''));
    returned.addEventListener('change', () => showError(''));

    // Same checks as the server (it checks again).
    form.addEventListener('submit', e => {
        let message = '';
        if (!online && !returned.checked) message = 'Return the money to the customer first, then tick the box.';
        else if (!reason.value) message = 'Choose a reason for the refund.';
        else if (reason.value === 'Other' && !note.value.trim()) message = 'Add a note when the reason is Other.';

        if (message) { e.preventDefault(); showError(message); return; }

        go.disabled = true;   // no double click; the server refunds a payment only once anyway
        try { sessionStorage.setItem('apScrollY', String(window.scrollY)); } catch (x) { /* ignore */ }
    });

    const close = () => dialog.close();
    dialog.querySelectorAll('[data-ap-close]').forEach(b => b.addEventListener('click', close));
    dialog.addEventListener('click', e => { if (e.target === dialog) close(); });
})();
