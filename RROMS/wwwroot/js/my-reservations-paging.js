// Owner: Member 2 — US15/US18. The Later list shows 5 bookings per page, with Show all / Show less.
(() => {
    'use strict';
    const PAGE_SIZE = 5;
    const list = document.querySelector('#myrList .myr-later-list');
    if (!list) return;
    const rows = [...list.querySelectorAll(':scope > .myr-later')];
    if (rows.length <= PAGE_SIZE) return;

    const pages = Math.ceil(rows.length / PAGE_SIZE);
    let page = 1;
    let showAll = false;

    const nav = document.createElement('nav');
    nav.className = 'myr-pager';
    nav.setAttribute('aria-label', 'Later reservations');
    const info = document.createElement('span');
    info.className = 'myr-pager-info';
    info.setAttribute('aria-live', 'polite');
    const numbers = document.createElement('div');
    numbers.className = 'myr-pager-pages';
    const toggle = document.createElement('button');
    toggle.type = 'button';
    toggle.className = 'myr-pager-all';
    nav.append(info, numbers, toggle);
    list.after(nav);

    const step = (label, aria, target) => {
        const b = document.createElement('button');
        b.type = 'button';
        b.className = 'myr-pager-step';
        const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
        svg.setAttribute('viewBox', '0 0 24 24');
        svg.setAttribute('aria-hidden', 'true');
        const path = document.createElementNS('http://www.w3.org/2000/svg', 'path');
        path.setAttribute('d', label === 'prev' ? 'm15 6-6 6 6 6' : 'm9 6 6 6-6 6');
        svg.append(path);
        b.append(svg);
        b.setAttribute('aria-label', aria);
        b.disabled = target < 1 || target > pages;
        b.dataset.page = String(target);
        return b;
    };

    function render() {
        const from = showAll ? 0 : (page - 1) * PAGE_SIZE;
        const to = showAll ? rows.length : Math.min(rows.length, from + PAGE_SIZE);
        rows.forEach((row, i) => row.classList.toggle('is-paged-out', i < from || i >= to));
        info.textContent = showAll
            ? 'Showing all ' + rows.length
            : 'Showing ' + (from + 1) + '–' + to + ' of ' + rows.length;
        numbers.hidden = showAll;
        numbers.replaceChildren();
        if (!showAll) {
            numbers.append(step('prev', 'Previous page', page - 1));
            for (let n = 1; n <= pages; n++) {
                const b = document.createElement('button');
                b.type = 'button';
                b.className = 'myr-pager-num' + (n === page ? ' is-on' : '');
                b.textContent = String(n);
                b.dataset.page = String(n);
                b.setAttribute('aria-label', 'Page ' + n);
                if (n === page) b.setAttribute('aria-current', 'page');
                numbers.append(b);
            }
            numbers.append(step('next', 'Next page', page + 1));
        }
        toggle.textContent = showAll ? 'Show less' : 'Show all';
        toggle.setAttribute('aria-pressed', String(showAll));
    }

    numbers.addEventListener('click', event => {
        const b = event.target.closest('button[data-page]');
        if (!b || b.disabled) return;
        page = Number(b.dataset.page);
        render();
        numbers.querySelector('[aria-current="page"]')?.focus({ preventScroll: true });
    });
    toggle.addEventListener('click', () => {
        showAll = !showAll;
        if (!showAll) page = 1;
        render();
        if (!showAll) list.closest('.myr-later-section')?.scrollIntoView({ block: 'start', behavior: 'smooth' });
    });
    render();
})();
