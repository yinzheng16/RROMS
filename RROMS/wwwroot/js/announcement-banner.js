// US39 (Member 4): home page announcement banner.
// 2 or more live announcements take turns: slide left every N seconds (data-seconds),
// ‹ › to move by hand, paused while the mouse or keyboard focus is on the banner,
// and not moving at all for visitors who turned animations off.
// Endless: after the last one the first slides in from the right (it never runs back to the start).
// How: a copy of the last slide sits before the first and a copy of the first after the last;
// when a move ends on a copy, the track jumps (without animation) to the real slide it copies.
(() => {
    'use strict';
    const bar = document.querySelector('[data-ab]');
    const track = bar?.querySelector('[data-ab-track]');
    if (!track) return;

    const real = [...track.querySelectorAll('[data-ab-slide]')];
    const n = real.length;
    if (n < 2) return;

    const ms = Number(bar.dataset.seconds || 6) * 1000;
    const still = window.matchMedia('(prefers-reduced-motion: reduce)').matches;

    // copies at both ends (hidden from screen readers and the keyboard)
    const copy = slide => {
        const c = slide.cloneNode(true);
        c.removeAttribute('data-ab-slide');
        c.setAttribute('aria-hidden', 'true');
        c.querySelectorAll('a').forEach(a => a.setAttribute('tabindex', '-1'));
        return c;
    };
    track.insertBefore(copy(real[n - 1]), real[0]);
    track.appendChild(copy(real[0]));

    // Member 1 restyle: "1 / 2" counter and a thin progress line that fills while a slide is shown.
    const now = bar.querySelector('[data-ab-now]');
    const progress = bar.querySelector('[data-ab-progress]');
    function restartProgress() {
        if (!progress || still) return;
        progress.classList.remove('is-run');
        void progress.offsetWidth;              // restart the CSS animation
        if (!held) progress.classList.add('is-run');
    }

    let pos = 1;            // position in the track: 0 = copy of last, 1..n = real, n + 1 = copy of first
    let moving = false, timer = null, held = false;

    function place(p, animate) {
        track.style.transition = animate ? '' : 'none';
        track.style.transform = `translateX(-${p * 100}%)`;
        if (!animate) void track.offsetWidth;   // apply the jump before the transition comes back
        pos = p;
        const shown = ((p - 1) % n + n) % n;    // which real slide is visible
        if (now) now.textContent = String(shown + 1);   // "1 / 2" counter (Member 1 restyle)
        restartProgress();
        real.forEach((s, x) => {
            s.setAttribute('aria-hidden', x === shown ? 'false' : 'true');
            s.querySelectorAll('a').forEach(a => x === shown ? a.removeAttribute('tabindex') : a.setAttribute('tabindex', '-1'));
        });
    }

    function move(step) {
        if (moving) return;                      // one move at a time (fast clicks)
        if (still) { place(((pos - 1 + step) % n + n) % n + 1, false); return; }
        moving = true;
        place(pos + step, true);
        clearTimeout(settleTimer);
        settleTimer = setTimeout(settle, 900);   // in case transitionend never comes (e.g. hidden tab)
    }

    let settleTimer = null;
    function settle() {
        clearTimeout(settleTimer);
        moving = false;
        if (pos === n + 1) place(1, false);      // on the copy of the first -> the real first
        else if (pos === 0) place(n, false);     // on the copy of the last  -> the real last
    }

    track.addEventListener('transitionend', e => { if (e.target === track) settle(); });

    function start() {
        clearInterval(timer);
        if (!still && !held) timer = setInterval(() => move(1), ms);
    }

    bar.querySelector('[data-ab-prev]')?.addEventListener('click', () => { move(-1); start(); });
    bar.querySelector('[data-ab-next]')?.addEventListener('click', () => { move(1); start(); });

    const hold = on => {
        held = on;
        bar.classList.toggle('is-held', on);   // pauses the progress line
        if (on) { clearInterval(timer); return; }
        start();
        restartProgress();                     // the timer starts again, so the line does too
    };
    bar.addEventListener('mouseenter', () => hold(true));
    bar.addEventListener('mouseleave', () => hold(false));
    bar.addEventListener('focusin', () => hold(true));
    bar.addEventListener('focusout', e => { if (!bar.contains(e.relatedTarget)) hold(false); });
    document.addEventListener('visibilitychange', () => document.hidden ? clearInterval(timer) : start());

    place(1, false);
    start();
})();
