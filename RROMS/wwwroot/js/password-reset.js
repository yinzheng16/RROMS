// US52 (Member 1): Forgot / Reset password pages. Eye buttons, live password rules, quick checks before sending.
// Same behaviour as the Security pop-ups on My profile; the server still checks everything.
(function () {
    'use strict';

    var RULES = {
        len: function (v) { return v.length >= 8; },
        upper: function (v) { return /[A-Z]/.test(v); },
        lower: function (v) { return /[a-z]/.test(v); },
        digit: function (v) { return /[0-9]/.test(v); },
        symbol: function (v) { return /[^A-Za-z0-9]/.test(v); }
    };

    function missing(v) {
        if (!RULES.len(v)) return 'at least 8 characters';
        if (!RULES.upper(v)) return 'a capital letter';
        if (!RULES.lower(v)) return 'a small letter';
        if (!RULES.digit(v)) return 'a number';
        return 'a symbol';
    }

    function showError(input, message) {
        var field = input.closest('.sx-field');
        field.classList.add('bad');
        field.querySelector('[data-err]').textContent = message;
        input.focus();
    }

    // Eye: slashed eye while hidden, open eye while shown (same as Login / Register / My profile)
    document.querySelectorAll('[data-sx-eye]').forEach(function (btn) {
        btn.addEventListener('click', function () {
            var input = btn.parentElement.querySelector('input');
            var show = input.type === 'password';
            input.type = show ? 'text' : 'password';
            btn.classList.toggle('on', show);
            btn.setAttribute('aria-label', show ? 'Hide password' : 'Show password');
        });
    });

    document.querySelectorAll('form[data-sx-reset]').forEach(function (form) {
        var submit = form.querySelector('[data-sx-submit]');

        // typing in a field clears its red message
        form.addEventListener('input', function (e) {
            var field = e.target.closest('.sx-field');
            if (field && field.classList.contains('bad')) {
                field.classList.remove('bad');
                field.querySelector('[data-err]').textContent = '';
            }
            update();
        });

        var next = form.querySelector('[name=NewPassword]');
        var confirm = form.querySelector('[name=ConfirmPassword]');

        function update() {
            if (!next) return;
            var v = next.value, met = 0;
            Object.keys(RULES).forEach(function (k) {
                var ok = RULES[k](v);
                if (ok) met++;
                var li = form.querySelector('[data-rule="' + k + '"]');
                if (li) li.classList.toggle('ok', ok);
            });
            var bars = v.length === 0 ? 0 : met >= 5 ? 4 : met >= 4 ? 3 : met >= 3 ? 2 : 1;
            var meter = form.querySelector('[data-sx-meter]');
            meter.querySelectorAll('b').forEach(function (b, i) { b.classList.toggle('on', i < bars); });
            meter.classList.toggle('strong', met >= 5);
            form.querySelector('[data-sx-strength]').innerHTML = v.length === 0 ? 'Start typing'
                : met >= 5 ? '<b>Strong</b>' : 'Almost there: add ' + missing(v);

            var same = confirm.value.length > 0 && confirm.value === v;
            form.querySelector('[data-sx-match]').hidden = !same;
            confirm.closest('.sx-field').classList.toggle('good', same);
        }
        update();

        form.addEventListener('submit', function (e) {
            if (form.dataset.sxReset === 'email') {
                var email = form.querySelector('[name=Email]');
                if (!email.value.trim()) { e.preventDefault(); return showError(email, 'Please enter your email.'); }
                if (!email.checkValidity()) { e.preventDefault(); return showError(email, 'Please enter a valid email address, e.g. name@example.com.'); }
            } else {
                var v = next.value;
                if (Object.keys(RULES).some(function (k) { return !RULES[k](v); })) {
                    e.preventDefault(); return showError(next, 'Your new password needs ' + missing(v) + '.');
                }
                if (v !== confirm.value) { e.preventDefault(); return showError(confirm, "The two new passwords don't match."); }
            }
            submit.disabled = true;           // no double sending
            submit.classList.add('busy');
        });
    });
})();
