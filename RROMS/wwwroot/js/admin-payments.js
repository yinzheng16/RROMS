// US40 Admin → Payments (Owner: Member 4)
// 1) Keep the scroll position when a filter, search or quick date is used
//    (the page reloads with the new filters but stays where the admin was).
// 2) Receipt button: preview the PDF in a pop-up; the admin decides to download.
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
