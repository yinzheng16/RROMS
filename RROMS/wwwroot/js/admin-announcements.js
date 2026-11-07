// US39 Admin → Announcements (Member 4)
// - "New announcement" / Edit open the form pop-up (no page reload); without JavaScript the links
//   reload the page with ?New=true / ?Edit=5 and the server opens it.
// - Send and Delete ask in a YORU pop-up first; the button is disabled after one click.
// - Live character counts. A sent announcement can still be edited (banner only); the form says so.
// - Copy opens a new form filled from an announcement (to send a correction or run it again).
(() => {
    'use strict';

    const formWrap = document.getElementById('anForm');
    const sendWrap = document.getElementById('anSend');
    const deleteWrap = document.getElementById('anDelete');
    if (!formWrap) return;

    const form = formWrap.querySelector('[data-an-form]');
    const field = name => form.querySelector(`[data-an-field="${name}"]`);
    let lastFocus = null;

    // ---------- open / close ----------
    function open(wrap) {
        lastFocus = document.activeElement;
        wrap.hidden = false;
        document.body.classList.add('an-modal-open');
        const first = wrap.querySelector('input:not([type=hidden]):not([readonly]), textarea:not([readonly]), select:not(:disabled), [data-an-send-go], [data-an-delete-go]');
        (first || wrap.querySelector('.an-x'))?.focus();
    }

    function close(wrap) {
        wrap.hidden = true;
        if (!document.querySelector('.an-modal-wrap:not([hidden])')) document.body.classList.remove('an-modal-open');
        lastFocus?.focus?.();
    }

    document.querySelectorAll('.an-modal-wrap').forEach(wrap => {
        wrap.addEventListener('click', e => {
            if (e.target === wrap) { close(wrap); return; }             // click on the dim area
            const x = e.target.closest('[data-an-close]');
            if (x) { e.preventDefault(); close(wrap); }
        });
    });

    document.addEventListener('keydown', e => {
        if (e.key !== 'Escape') return;
        const top = [...document.querySelectorAll('.an-modal-wrap:not([hidden])')].pop();
        if (top) close(top);
    });

    // ---------- character counts ----------
    function count(box) {
        const out = box.closest('.an-f')?.querySelector('[data-an-counter]');
        if (!out) return;
        const max = Number(box.dataset.anCount);
        out.textContent = `${box.value.length} / ${max}`;
        out.classList.toggle('is-max', box.value.length >= max);
    }

    form.querySelectorAll('[data-an-count]').forEach(box => {
        box.addEventListener('input', () => {
            count(box);
            box.classList.remove('is-bad');
        });
    });

    // ---------- note when already sent ----------
    function lock(sent) {
        form.querySelector('[data-an-locked]').hidden = !sent;
    }

    // ---------- fill the form (new / edit) ----------
    function fill(data) {
        const editing = data.id !== '0';
        field('id').value = data.id;
        field('sent').value = data.sent ? 'True' : 'False';
        field('title').value = data.title;
        field('message').value = data.message;
        field('start').value = data.start;
        field('end').value = data.end;
        field('link').value = data.link;
        lock(data.sent);

        formWrap.querySelector('[data-an-form-kicker]').textContent = editing ? 'EDIT ANNOUNCEMENT · 編集' : 'NEW ANNOUNCEMENT · 新しいお知らせ';
        formWrap.querySelector('[data-an-form-title]').textContent = editing ? 'Edit announcement' : (data.copy ? 'Copy announcement' : 'Tell your guests');
        form.querySelector('[data-an-submit]').textContent = editing ? 'Save changes' : 'Save announcement';

        // a fresh form: remove any old server error
        form.querySelectorAll('.an-err, .an-alert').forEach(el => el.remove());
        form.querySelectorAll('.is-bad').forEach(el => el.classList.remove('is-bad'));
        form.querySelectorAll('[data-an-count]').forEach(count);
    }

    // "2026-11-03T18:00" (the value a datetime-local box takes)
    const iso = d => {
        const z = n => String(n).padStart(2, '0');
        return `${d.getFullYear()}-${z(d.getMonth() + 1)}-${z(d.getDate())}T${z(d.getHours())}:${z(d.getMinutes())}`;
    };

    document.querySelector('[data-an-new]')?.addEventListener('click', e => {
        e.preventDefault();
        // From now until 11:59 PM a week later.
        const now = new Date();
        const week = new Date(now); week.setDate(now.getDate() + 7); week.setHours(23, 59, 0, 0);
        fill({ id: '0', sent: false, title: '', message: '', start: iso(now), end: iso(week), link: 'None' });
        open(formWrap);
    });

    document.querySelectorAll('[data-an-edit]').forEach(a => {
        a.addEventListener('click', e => {
            e.preventDefault();
            const d = a.dataset;
            fill({ id: d.id, sent: d.sent === 'true', title: d.title, message: d.message, start: d.start, end: d.end, link: d.link });
            open(formWrap);
        });
    });

    document.querySelectorAll('[data-an-copy]').forEach(a => {
        a.addEventListener('click', e => {
            e.preventDefault();
            const d = a.dataset;
            // A start that passed moves to now, keeping the same length.
            let start = d.start, end = d.end;
            const now = new Date(); now.setSeconds(0, 0);
            if (new Date(d.start) < now) {
                const last = new Date(now.getTime() + Number(d.minutes || 0) * 60000);
                start = iso(now); end = iso(last);
            }
            fill({ id: '0', sent: false, copy: true, title: d.title, message: d.message, start, end, link: d.link });
            open(formWrap);
        });
    });

    // Opened by the server (no-JS link or a problem when saving): show the note and focus it.
    if (!formWrap.hidden) {
        lock(field('sent').value.toLowerCase() === 'true');
        form.querySelectorAll('[data-an-count]').forEach(count);
        document.body.classList.add('an-modal-open');
        (form.querySelector('.is-bad') || field('title'))?.focus();
    }

    // Browser-side date check (the server checks again).
    form.addEventListener('submit', e => {
        const start = field('start').value, end = field('end').value;
        if (start && end && end <= start) {
            e.preventDefault();
            const box = field('end');
            box.classList.add('is-bad');
            if (!box.parentElement.querySelector('.an-err')) {
                box.insertAdjacentHTML('afterend', '<p class="an-err"><i class="bi bi-exclamation-circle" aria-hidden="true"></i>The end must be after the start.</p>');
            }
            box.focus();
            return;
        }
        form.querySelector('[data-an-submit]').disabled = true;   // one save per click
    });

    // ---------- "On the home page now": the small banner takes turns like the real one ----------
    (() => {
        const panel = document.querySelector('[data-an-mini]');
        const list = panel?.querySelector('.an-rot');
        if (!list) return;   // 0 or 1 live: nothing to turn
        const items = [...list.querySelectorAll('[data-an-mini-item]')];
        const title = panel.querySelector('[data-an-mini-title]');
        const button = panel.querySelector('[data-an-mini-btn]');
        const ms = Number(list.dataset.seconds || 6) * 1000;
        let i = 0, timer = null;

        function show(k) {
            i = (k + items.length) % items.length;
            const d = items[i].dataset;
            title.textContent = d.title;
            button.textContent = d.button || '';
            button.hidden = !d.button;
            items.forEach((b, x) => b.closest('li').classList.toggle('is-on', x === i));
        }
        const start = () => { clearInterval(timer); timer = setInterval(() => show(i + 1), ms); };

        items.forEach((b, x) => b.addEventListener('click', () => { show(x); start(); }));
        start();
    })();

    // ---------- Send / Delete confirm ----------
    function confirmWith(wrap, titleSlot, goButton, selector) {
        if (!wrap) return;
        let pending = null;

        document.querySelectorAll(selector).forEach(f => {
            f.addEventListener('submit', e => {
                if (f.dataset.confirmed) return;   // already confirmed: let it post
                e.preventDefault();
                pending = f;
                wrap.querySelector(titleSlot).textContent = f.dataset.title;
                open(wrap);
            });
        });

        wrap.querySelector(goButton).addEventListener('click', e => {
            if (!pending) return;
            e.currentTarget.disabled = true;
            pending.dataset.confirmed = 'true';
            pending.querySelectorAll('button').forEach(b => b.disabled = true);
            pending.submit();
        });
    }

    confirmWith(sendWrap, '[data-an-send-title]', '[data-an-send-go]', 'form[data-an-send]');
    confirmWith(deleteWrap, '[data-an-delete-title]', '[data-an-delete-go]', 'form[data-an-delete]');

    // Stop / Resume: one post per click.
    document.querySelectorAll('.an-acts form:not([data-an-send]):not([data-an-delete])').forEach(f => {
        f.addEventListener('submit', () => f.querySelectorAll('button').forEach(b => b.disabled = true));
    });
})();
