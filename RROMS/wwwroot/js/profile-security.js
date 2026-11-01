// US03 / US04 (Member 1): Security pop-ups on My profile (Change password · Change email).
// Opened by buttons with data-sx-open="sxPwd" / "sxEmail". Posts with fetch() and shows the answer in place.
(function () {
    'use strict';

    var RULES = {
        len: function (v) { return v.length >= 8; },
        upper: function (v) { return /[A-Z]/.test(v); },
        lower: function (v) { return /[a-z]/.test(v); },
        digit: function (v) { return /[0-9]/.test(v); },
        symbol: function (v) { return /[^A-Za-z0-9]/.test(v); }
    };
    var lastOpener = null;

    function $(root, sel) { return root.querySelector(sel); }
    function $$(root, sel) { return Array.prototype.slice.call(root.querySelectorAll(sel)); }

    // ---------- open / close ----------
    function open(modal, opener) {
        lastOpener = opener || document.activeElement;
        reset(modal);
        modal.classList.add('show');
        modal.setAttribute('aria-hidden', 'false');
        document.body.style.overflow = 'hidden';
        var first = $(modal, 'form input:not([type=hidden])');
        if (first) setTimeout(function () { first.focus(); }, 30);
    }

    function close(modal) {
        modal.classList.remove('show');
        modal.setAttribute('aria-hidden', 'true');
        document.body.style.overflow = '';
        if (lastOpener && lastOpener.focus) lastOpener.focus();
    }

    function reset(modal) {
        var form = $(modal, 'form');
        form.reset();
        form.hidden = false;
        $(modal, '[data-sx-done]').hidden = true;
        clearErrors(modal);
        var tries = $(modal, '[data-sx-tries]');
        if (tries) tries.hidden = true;
        var submit = $(modal, '[data-sx-submit]');
        submit.disabled = false;
        submit.classList.remove('busy');
        $$(modal, '[data-sx-eye]').forEach(function (b) { showPassword(b, false); });
        var stamp = $(modal, '[data-sx-stamp]');
        stamp.classList.remove('is-done');
        var kanji = $(modal, '[data-sx-kanji]');
        kanji.textContent = kanji.dataset.first || (kanji.dataset.first = kanji.textContent);
        setSteps(modal, 0, 0);
        artText(modal, false);
        var newBox = $(modal, '[data-sx-new]');
        if (newBox) newBox.textContent = '';
        var resent = $(modal, '[data-sx-resent]');
        if (resent) resent.textContent = '';
        updatePassword(modal);
    }

    // left panel title / Japanese line / text: normal or "done" wording (data-done)
    function artText(modal, isDone) {
        $$(modal, '[data-sx-art-title],[data-sx-art-jp],[data-sx-art-text]').forEach(function (el) {
            if (el.dataset.first === undefined) el.dataset.first = el.innerHTML;
            el.innerHTML = isDone ? el.dataset.done : el.dataset.first;
        });
    }

    // steps: "done" = how many are finished, "on" = which one is current
    function setSteps(modal, done, on) {
        $$(modal, '[data-sx-steps] li').forEach(function (li, i) {
            li.classList.toggle('done', i < done);
            li.classList.toggle('on', i === on && i >= done);
        });
    }

    // ---------- errors ----------
    function clearErrors(modal) {
        $$(modal, '.sx-field').forEach(function (f) {
            f.classList.remove('bad');
            var e = $(f, '[data-err]');
            if (e) e.textContent = '';
        });
        $(modal, '[data-sx-general]').textContent = '';
    }

    function showError(modal, field, message) {
        var box = field ? $(modal, '.sx-field[data-field="' + field + '"]') : null;
        if (box) {
            box.classList.add('bad');
            $(box, '[data-err]').textContent = message;
            var input = $(box, 'input');
            if (input) input.focus();
        } else {
            $(modal, '[data-sx-general]').textContent = message;
        }
    }

    function showTries(modal, left, locked) {
        var box = $(modal, '[data-sx-tries]');
        if (!box || left === null || left === undefined) return;
        box.hidden = false;
        var used = 5 - left;
        $$(box, '.sx-tries b').forEach(function (b, i) { b.classList.toggle('used', i < used); });
        $(box, '[data-sx-tries-text]').textContent = locked
            ? 'Locked for ' + locked + ' minute' + (locked === 1 ? '' : 's')
            : left + (left === 1 ? ' try left' : ' tries left');
        $(box, '[data-sx-tries-help]').textContent = locked
            ? 'Too many wrong passwords. Please try again later.'
            : 'After 5 wrong tries, your account is locked for 15 minutes.';
        if (locked) $(modal, '[data-sx-submit]').disabled = true;
    }

    // ---------- show / hide password ----------
    function showPassword(btn, on) {
        var input = btn.parentElement.querySelector('input');
        input.type = on ? 'text' : 'password';
        btn.setAttribute('aria-label', on ? 'Hide password' : 'Show password');
        btn.classList.toggle('on', on);
    }

    // ---------- live password rules ----------
    function updatePassword(modal) {
        var current = $(modal, '[name=currentPassword]');
        var next = $(modal, '[name=newPassword]');
        if (!next) return;
        var confirm = $(modal, '[name=confirmPassword]');
        var v = next.value, met = 0;

        Object.keys(RULES).forEach(function (k) {
            var ok = RULES[k](v);
            if (ok) met++;
            var li = $(modal, '[data-rule="' + k + '"]');
            if (li) li.classList.toggle('ok', ok);
        });
        var diff = v.length > 0 && v !== current.value;
        $(modal, '[data-rule="diff"]').classList.toggle('ok', diff);

        // 4 bars: 2 rules = 1 bar ... all 5 = 4 bars (and green)
        var bars = v.length === 0 ? 0 : met >= 5 ? 4 : met >= 4 ? 3 : met >= 3 ? 2 : 1;
        var meter = $(modal, '[data-sx-meter]');
        $$(meter, 'b').forEach(function (b, i) { b.classList.toggle('on', i < bars); });
        meter.classList.toggle('strong', met >= 5 && diff);
        var label = $(modal, '[data-sx-strength]');
        label.innerHTML = v.length === 0 ? 'Start typing'
            : met >= 5 && diff ? '<b>Strong</b>'
            : met >= 5 ? 'Must be different from your current one'
            : 'Almost there: ' + missing(v);

        var match = $(modal, '[data-sx-match]');
        var same = confirm.value.length > 0 && confirm.value === v;
        match.hidden = !same;
        $(modal, '.sx-field[data-field="ConfirmPassword"]').classList.toggle('good', same);

        // steps: 1 = current password typed, 2 = new password valid
        var step1 = current.value.length > 0, step2 = met >= 5 && diff && same;
        setSteps(modal, step1 ? (step2 ? 2 : 1) : 0, step1 ? (step2 ? 2 : 1) : 0);
    }

    function missing(v) {
        if (!RULES.len(v)) return 'at least 8 characters';
        if (!RULES.upper(v)) return 'add a capital letter';
        if (!RULES.lower(v)) return 'add a small letter';
        if (!RULES.digit(v)) return 'add a number';
        return 'add a symbol';
    }

    // ---------- submit ----------
    function submit(modal, form, isResend) {
        var btn = $(modal, '[data-sx-submit]');
        if (!isResend) clearErrors(modal);

        // quick checks before asking the server
        if (!isResend && form.dataset.sxHandler === 'ChangePassword') {
            var v = $(form, '[name=newPassword]').value;
            if (!$(form, '[name=currentPassword]').value) return showError(modal, 'CurrentPassword', 'Please enter your current password.');
            if (Object.keys(RULES).some(function (k) { return !RULES[k](v); }))
                return showError(modal, 'NewPassword', 'Your new password needs ' + missing(v).replace('add ', '') + '.');
            if (v !== $(form, '[name=confirmPassword]').value) return showError(modal, 'ConfirmPassword', "The two new passwords don't match.");
        }
        if (!isResend && form.dataset.sxHandler === 'ChangeEmail') {
            var email = $(form, '[name=newEmail]');
            if (!email.value.trim()) return showError(modal, 'NewEmail', 'Please enter your new email.');
            if (!email.checkValidity()) return showError(modal, 'NewEmail', 'Please enter a valid email address, e.g. name@example.com.');
            if (!$(form, '[name=password]').value) return showError(modal, 'Password', 'Please enter your password.');
        }

        btn.disabled = true;
        btn.classList.add('busy');
        return fetch(location.pathname + '?handler=' + form.dataset.sxHandler, {
            method: 'POST',
            body: new FormData(form),
            headers: { 'X-Requested-With': 'fetch' },
            credentials: 'same-origin'
        })
            .then(function (r) {
                if (!r.ok) throw new Error('HTTP ' + r.status);
                return r.json();
            })
            .then(function (res) {
                btn.classList.remove('busy');
                btn.disabled = false;
                if (res.ok) return done(modal, form, res, isResend);
                if (isResend) {
                    // a resend that fails: go back to the form so the message is visible
                    form.hidden = false;
                    $(modal, '[data-sx-done]').hidden = true;
                }
                showError(modal, res.field, res.message || 'Something went wrong. Please try again.');
                if (res.triesLeft !== null && res.triesLeft !== undefined) showTries(modal, res.triesLeft, res.lockedMinutes);
            })
            .catch(function () {
                btn.classList.remove('busy');
                btn.disabled = false;
                showError(modal, null, "We couldn't reach the server. Check your connection and try again.");
            });
    }

    function done(modal, form, res, isResend) {
        if (isResend) {
            $(modal, '[data-sx-resent]').textContent = '✓ Sent again';
            return;
        }
        form.hidden = true;
        var pane = $(modal, '[data-sx-done]');
        pane.hidden = false;
        $(pane, '[data-sx-done-email]').textContent = res.email || '';

        // stamp turns green with 済, steps move on
        $(modal, '[data-sx-stamp]').classList.add('is-done');
        $(modal, '[data-sx-kanji]').textContent = '済';
        artText(modal, true);
        if (form.dataset.sxHandler === 'ChangePassword') {
            setSteps(modal, 3, -1);
        } else {
            setSteps(modal, 1, 1);
        }
        var main = $(pane, '.sx-btn.main');
        if (main) main.focus();
    }

    // ---------- wire up ----------
    document.addEventListener('DOMContentLoaded', function () {
        $$(document, '[data-sx-open]').forEach(function (btn) {
            btn.addEventListener('click', function (e) {
                e.preventDefault();
                var modal = document.getElementById(btn.dataset.sxOpen);
                if (modal) open(modal, btn);
            });
        });

        $$(document, '.sx').forEach(function (modal) {
            var form = $(modal, 'form');

            $$(modal, '[data-sx-close]').forEach(function (b) { b.addEventListener('click', function () { close(modal); }); });
            modal.addEventListener('mousedown', function (e) { if (e.target === modal) close(modal); });

            $$(modal, '[data-sx-eye]').forEach(function (b) {
                b.addEventListener('click', function () { showPassword(b, b.parentElement.querySelector('input').type === 'password'); });
            });

            form.addEventListener('input', function (e) {
                var field = e.target.closest('.sx-field');
                if (field && field.classList.contains('bad')) {
                    field.classList.remove('bad');
                    $(field, '[data-err]').textContent = '';
                }
                updatePassword(modal);
                var newBox = $(modal, '[data-sx-new]');
                if (newBox && e.target.name === 'newEmail') {
                    newBox.textContent = e.target.value.trim();
                    setSteps(modal, 0, 0);
                }
            });

            form.addEventListener('submit', function (e) {
                e.preventDefault();
                submit(modal, form, false);
            });

            var resend = $(modal, '[data-sx-resend]');
            if (resend) resend.addEventListener('click', function () {
                $(modal, '[data-sx-resent]').textContent = 'Sending…';
                submit(modal, form, true);
            });

            // keep keyboard focus inside the pop-up
            modal.addEventListener('keydown', function (e) {
                if (e.key === 'Escape') { e.stopPropagation(); close(modal); return; }
                if (e.key !== 'Tab') return;
                var items = $$(modal, 'button, a[href], input:not([type=hidden])').filter(function (el) {
                    return el.offsetParent !== null && !el.disabled;
                });
                if (!items.length) return;
                var first = items[0], last = items[items.length - 1];
                if (e.shiftKey && document.activeElement === first) { e.preventDefault(); last.focus(); }
                else if (!e.shiftKey && document.activeElement === last) { e.preventDefault(); first.focus(); }
            });
        });
    });
})();
