// US05 / US07 Admin → Users (Owner: Member 1)
// Pop-ups (Add user, Change role, Deactivate / Reactivate / Send link), status filter, country picker.
// Everything is checked again on the server (UserAdminService); this file only makes the page nicer to use.
(function () {
    "use strict";

    var page = document.getElementById("uuPage");
    if (!page) { return; }

    // Refresh (F5 / reload button) starts again with an empty search and no filters.
    // Back / Forward and links still keep them.
    try {
        var nav = performance.getEntriesByType("navigation")[0];
        if (nav && nav.type === "reload" && location.search) {
            location.replace(location.pathname);
            return;
        }
    } catch (e) { /* old browser: keep the filters */ }

    var lastFocus = null;

    // ---------------- pop-ups ----------------
    function openModal(modal) {
        lastFocus = document.activeElement;
        modal.hidden = false;
        document.body.classList.add("uu-lock");
        var first = modal.querySelector("[autofocus], input:not([type=hidden]):not([type=radio]):not([disabled]), input[type=radio]:checked, button[type=submit]:not([disabled])");
        setTimeout(function () { (first || modal.querySelector(".uu-x")).focus(); }, 30);
    }

    function closeModal(modal) {
        if (!modal || modal.hidden) { return; }
        var form = modal.querySelector("form.is-busy");
        if (form) { return; }   // saving: wait for the page to reload
        modal.hidden = true;
        if (!document.querySelector(".uu-modal:not([hidden])")) { document.body.classList.remove("uu-lock"); }
        if (lastFocus && lastFocus.focus) { lastFocus.focus(); }
    }

    document.querySelectorAll(".uu-modal").forEach(function (modal) {
        modal.querySelectorAll("[data-uu-close]").forEach(function (el) {
            el.addEventListener("click", function () { closeModal(modal); });
        });

        // keep Tab inside the pop-up
        modal.addEventListener("keydown", function (e) {
            if (e.key === "Escape") {
                // let the country list close first
                if (modal.querySelector(".cc.is-open")) { return; }
                closeModal(modal);
                return;
            }
            if (e.key !== "Tab") { return; }
            var items = Array.prototype.filter.call(
                modal.querySelectorAll("button:not([disabled]), input:not([type=hidden]):not([disabled]), select:not(.cc-native), a[href]"),
                function (el) { return el.offsetParent !== null; });
            if (!items.length) { return; }
            var firstEl = items[0], lastEl = items[items.length - 1];
            if (e.shiftKey && document.activeElement === firstEl) { e.preventDefault(); lastEl.focus(); }
            else if (!e.shiftKey && document.activeElement === lastEl) { e.preventDefault(); firstEl.focus(); }
        });
    });

    document.querySelectorAll("[data-uu-open]").forEach(function (btn) {
        btn.addEventListener("click", function () {
            var modal = document.getElementById(btn.getAttribute("data-uu-open"));
            if (modal) { openModal(modal); }
        });
    });

    // Add user comes back open when the server found a problem (e.g. email already used).
    var add = document.getElementById("uuAdd");
    if (add && add.getAttribute("data-uu-start-open") === "true") {
        openModal(add);
        var bad = add.querySelector(".uu-field.bad input:not([type=radio])");
        if (bad) { setTimeout(function () { bad.focus(); }, 40); }
    }

    // "Your password" only when Admin is picked (US05: adding / promoting an Admin).
    function wireAdminPassword(modal, radioName) {
        var box = modal.querySelector("[data-uu-admin-only]");
        if (!box) { return function () { }; }
        var input = box.querySelector("input");
        function sync() {
            var picked = modal.querySelector("input[name='" + radioName + "']:checked");
            var isAdmin = !!picked && picked.value === "Admin" && !(picked.closest(".uu-rc") || {}).classList.contains("is-current");
            box.hidden = !isAdmin;
            input.required = isAdmin;
            if (!isAdmin) { input.value = ""; }
        }
        modal.addEventListener("change", function (e) {
            if (e.target.name === radioName) {
                sync();
                if (!box.hidden) { input.focus(); }
            }
        });
        return sync;
    }
    if (add) {
        var syncAddPw = wireAdminPassword(add, "NewUser.Role");
        if (add.querySelector("[data-uu-admin-only].bad") === null) { syncAddPw(); }
    }

    // Clear a field's red message as soon as it is edited.
    if (add) {
        add.querySelectorAll(".uu-field").forEach(function (field) {
            field.addEventListener("input", function () {
                field.classList.remove("bad");
                var err = field.querySelector(".uu-err");
                if (err) { err.textContent = ""; }
            });
            field.addEventListener("change", function () { field.classList.remove("bad"); });
        });
    }

    // Spinner + no double submit.
    document.querySelectorAll("form[data-uu-busy]").forEach(function (form) {
        form.addEventListener("submit", function (e) {
            if (form.classList.contains("is-busy")) { e.preventDefault(); return; }
            form.classList.add("is-busy");
            var btn = form.querySelector("[data-uu-submit]");
            if (btn) {
                btn.classList.add("is-busy");
                setTimeout(function () { btn.disabled = true; }, 0);   // after the browser has read the form
            }
        });
    });

    // ---------------- Change role ----------------
    var roleModal = document.getElementById("uuRole");
    var syncRolePw = roleModal ? wireAdminPassword(roleModal, "newRole") : function () { };
    function openRole(btn) {
        var current = btn.getAttribute("data-role");
        roleModal.querySelector("[data-uu-id]").value = btn.getAttribute("data-id");
        roleModal.querySelector("[data-uu-name]").textContent = btn.getAttribute("data-name");
        var save = roleModal.querySelector("[data-uu-submit]");
        var full = roleModal.getAttribute("data-admins-full") === "true";
        roleModal.querySelectorAll("input[name=newRole]").forEach(function (radio) {
            var isCurrent = radio.value === current;
            var card = radio.closest(".uu-rc");
            radio.checked = isCurrent;
            card.classList.toggle("is-current", isCurrent);
            // Admin limit reached: can't make one more Admin
            var blocked = full && radio.value === "Admin" && !isCurrent;
            radio.disabled = blocked;
            card.classList.toggle("is-full", blocked);
            var small = card.querySelector("small");
            if (!small.dataset.text) { small.dataset.text = small.textContent; }
            small.textContent = blocked ? radio.dataset.fullText : small.dataset.text;
        });
        syncRolePw();
        save.disabled = true;   // nothing to save until another role is picked
        openModal(roleModal);
    }
    if (roleModal) {
        roleModal.addEventListener("change", function () {
            var picked = roleModal.querySelector("input[name=newRole]:checked");
            var current = roleModal.querySelector(".uu-rc.is-current input");
            roleModal.querySelector("[data-uu-submit]").disabled = !picked || (current && picked === current);
        });
    }

    // ---------------- Deactivate / Reactivate / Send link ----------------
    var confirmModal = document.getElementById("uuConfirm");
    var texts = {
        deactivate: {
            icon: "bi-slash-circle", tone: "is-danger", title: "Deactivate this account?", ok: "Deactivate",
            text: function (b) { return "<b>" + esc(b.dataset.name) + "</b> won't be able to log in, and will be logged out within a minute. Their reservations, orders and bills stay. You can reactivate the account any time."; }
        },
        reactivate: {
            icon: "bi-check-lg", tone: "is-go", title: "Reactivate this account?", ok: "Reactivate",
            text: function (b) { return "<b>" + esc(b.dataset.name) + "</b> will be able to log in again with their current password."; }
        },
        remove: {
            icon: "bi-trash3", tone: "is-danger", title: "Remove this account?", ok: "Remove account",
            text: function (b) { return "<b>" + esc(b.dataset.name) + "</b> (" + esc(b.dataset.role) + ") disappears from the list for good. Their email, phone and password are wiped, so the email can be used for a new account. Their name stays in old bills and logs. This can't be undone."; }
        },
        link: {
            icon: "bi-envelope-arrow-up", tone: "is-link", title: "Send a password link?", ok: "Send link",
            text: function (b) {
                return b.dataset.welcome === "true"
                    ? "<b>" + esc(b.dataset.name) + "</b> hasn't set a password yet. We'll email <b>" + esc(b.dataset.email) + "</b> a new set-password link (works once, for 1 hour)."
                    : "We'll email <b>" + esc(b.dataset.email) + "</b> a link to choose a new password (works once, for 1 hour). Their current password keeps working until they use it.";
            }
        }
    };

    function esc(s) {
        var d = document.createElement("div");
        d.textContent = s || "";
        return d.innerHTML;
    }

    function openConfirm(btn, act) {
        var t = texts[act];
        var form = confirmModal.querySelector("form");
        form.action = confirmModal.getAttribute("data-url-" + act);
        form.querySelector("[data-uu-id]").value = btn.getAttribute("data-id");
        var icon = confirmModal.querySelector("[data-uu-icon]");
        icon.className = "uu-cf-ic " + t.tone;
        icon.querySelector("i").className = "bi " + t.icon;
        confirmModal.querySelector("[data-uu-title]").textContent = t.title;
        confirmModal.querySelector("[data-uu-text]").innerHTML = t.text(btn);
        var ok = confirmModal.querySelector("[data-uu-submit]");
        ok.className = "uu-btn main " + t.tone;
        ok.querySelector("[data-uu-ok]").textContent = t.ok;
        openModal(confirmModal);
    }

    page.addEventListener("click", function (e) {
        var btn = e.target.closest("[data-uu-act]");
        if (!btn) { return; }
        var act = btn.getAttribute("data-uu-act");
        if (act === "role") { openRole(btn); }
        else if (texts[act]) { openConfirm(btn, act); }
    });

    // ---------------- eye: slashed while hidden, open while shown ----------------
    function hidePassword(btn) {
        btn.parentElement.querySelector("input").type = "password";
        btn.classList.remove("on");
        btn.setAttribute("aria-label", "Show password");
    }
    document.querySelectorAll("[data-uu-eye]").forEach(function (btn) {
        btn.addEventListener("click", function () {
            var input = btn.parentElement.querySelector("input");
            var show = input.type === "password";
            input.type = show ? "text" : "password";
            btn.classList.toggle("on", show);
            btn.setAttribute("aria-label", show ? "Hide password" : "Show password");
            input.focus();
        });
        // never leave a password showing: hide it again when the form is sent or the pop-up closes
        var form = btn.closest("form");
        if (form) { form.addEventListener("submit", function () { hidePassword(btn); }); }
        btn.closest(".uu-modal").querySelectorAll("[data-uu-close]").forEach(function (el) {
            el.addEventListener("click", function () { hidePassword(btn); });
        });
    });

    // ---------------- the account just added ----------------
    var fresh = document.querySelector("[data-uu-new]");
    if (fresh) { fresh.scrollIntoView({ block: "center" }); }

    // ---------------- filters ----------------
    document.querySelectorAll("[data-uu-autosubmit]").forEach(function (sel) {
        sel.addEventListener("change", function () { sel.form.submit(); });
    });

    // Empty search box -> leave it out of the address.
    var filters = document.querySelector("[data-uu-filters]");
    if (filters) {
        filters.addEventListener("submit", function () {
            filters.querySelectorAll("input, select").forEach(function (el) {
                if (el.name && !el.value) { el.disabled = true; }
            });
        });
    }

    // ---------------- country picker (same as /Account/Register, Member 1) ----------------
    document.querySelectorAll(".uu-dialog [data-cc]").forEach(function (box) {
        var select = box.querySelector(".cc-native");
        var btn = box.querySelector(".cc-btn");
        var panel = box.querySelector(".cc-panel");
        var search = box.querySelector(".cc-input");
        var list = box.querySelector(".cc-list");
        var empty = box.querySelector(".cc-empty");
        var phone = box.parentElement.querySelector("input[type=tel]");
        var active = -1;

        box.classList.add("cc-ready");

        var items = Array.prototype.map.call(select.options, function (opt) {
            var li = document.createElement("li");
            li.className = "cc-item";
            li.setAttribute("role", "option");
            li.dataset.value = opt.value;
            li.innerHTML = '<span class="cc-flag fi" aria-hidden="true"></span><span class="cc-name"><span></span><small class="cc-iso"></small></span><span class="cc-code"></span><i class="bi bi-check2 cc-tick" aria-hidden="true"></i>';
            li.querySelector(".cc-flag").classList.add("fi-" + opt.value.toLowerCase());
            li.querySelector(".cc-name span").textContent = opt.dataset.name;
            li.querySelector(".cc-iso").textContent = opt.value;
            li.querySelector(".cc-code").textContent = opt.dataset.dial;
            li.dataset.search = (opt.dataset.name + " " + opt.value + " " + opt.dataset.dial + " " + opt.dataset.dial.replace("+", "")).toLowerCase();
            li.addEventListener("click", function () { choose(opt.value); });
            list.appendChild(li);
            return li;
        });

        function current() { return select.options[select.selectedIndex]; }
        function paint() {
            var opt = current();
            btn.querySelector(".cc-flag").className = "cc-flag fi fi-" + opt.value.toLowerCase();
            btn.setAttribute("aria-label", "Country code: " + opt.dataset.name + " " + opt.dataset.dial);
            btn.querySelector(".cc-dial").textContent = opt.dataset.dial;
            items.forEach(function (li) {
                var on = li.dataset.value === opt.value;
                li.classList.toggle("is-selected", on);
                li.setAttribute("aria-selected", on);
            });
        }
        function visible() { return items.filter(function (li) { return !li.hidden; }); }
        function highlight(i) {
            var rows = visible();
            rows.forEach(function (li) { li.classList.remove("is-active"); });
            if (!rows.length) { active = -1; return; }
            active = (i + rows.length) % rows.length;
            rows[active].classList.add("is-active");
            rows[active].scrollIntoView({ block: "nearest" });
        }
        function filter() {
            var q = search.value.trim().toLowerCase().replace(/^\+/, "");
            items.forEach(function (li) { li.hidden = q !== "" && li.dataset.search.indexOf(q) === -1; });
            empty.hidden = visible().length > 0;
            highlight(0);
        }
        function open() {
            panel.hidden = false;
            btn.setAttribute("aria-expanded", "true");
            box.classList.add("is-open");
            search.value = "";
            filter();
            var sel = list.querySelector(".is-selected");
            if (sel) { highlight(visible().indexOf(sel)); }
            search.focus();
        }
        function close(back) {
            panel.hidden = true;
            btn.setAttribute("aria-expanded", "false");
            box.classList.remove("is-open");
            if (back) { btn.focus(); }
        }
        function choose(value) {
            select.value = value;
            select.dispatchEvent(new Event("change", { bubbles: true }));
            close(false);
            if (phone) { phone.focus(); }
        }

        select.addEventListener("change", paint);
        btn.addEventListener("click", function () { panel.hidden ? open() : close(true); });
        search.addEventListener("input", filter);
        search.addEventListener("keydown", function (e) {
            if (e.key === "ArrowDown") { e.preventDefault(); highlight(active + 1); }
            else if (e.key === "ArrowUp") { e.preventDefault(); highlight(active - 1); }
            else if (e.key === "Enter") { e.preventDefault(); var r = visible(); if (r[active]) { choose(r[active].dataset.value); } }
            else if (e.key === "Escape") { e.stopPropagation(); close(true); }
        });
        document.addEventListener("click", function (e) { if (!box.contains(e.target)) { close(false); } });
        paint();
    });
})();
