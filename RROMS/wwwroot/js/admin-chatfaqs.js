// US10 (Owner: Member 1): Admin → Chatbot FAQs.
// Add / Edit / Delete pop-ups, counters, keyword preview and "Try the assistant".
// Everything is checked again on the server (ChatFaqService).
(function () {
    "use strict";

    var page = document.getElementById("cfPage");
    if (!page) { return; }

    // Refresh (F5) starts again with no search or filter, like Admin → Users.
    try {
        var nav = performance.getEntriesByType("navigation")[0];
        if (nav && nav.type === "reload" && location.search) { location.replace(location.pathname); return; }
    } catch (e) { /* keep the filters */ }

    var lastFocus = null;

    // ---------------- pop-ups ----------------
    function openModal(modal, focusEl) {
        lastFocus = document.activeElement;
        modal.hidden = false;
        document.body.classList.add("uu-lock");
        setTimeout(function () { (focusEl || modal.querySelector("input:not([type=hidden]), textarea, button[type=submit]")).focus(); }, 30);
    }
    function closeModal(modal) {
        if (!modal || modal.hidden || modal.querySelector("form.is-busy")) { return; }
        modal.hidden = true;
        if (!document.querySelector(".uu-modal:not([hidden])")) { document.body.classList.remove("uu-lock"); }
        if (lastFocus && lastFocus.focus) { lastFocus.focus(); }
    }
    document.querySelectorAll(".uu-modal").forEach(function (modal) {
        modal.querySelectorAll("[data-uu-close]").forEach(function (el) {
            el.addEventListener("click", function () { closeModal(modal); });
        });
        modal.addEventListener("keydown", function (e) {
            if (e.key === "Escape") { closeModal(modal); }
        });
    });

    // spinner + no double submit
    document.querySelectorAll("form[data-uu-busy]").forEach(function (form) {
        form.addEventListener("submit", function (e) {
            if (form.classList.contains("is-busy")) { e.preventDefault(); return; }
            form.classList.add("is-busy");
            var btn = form.querySelector("[data-uu-submit]");
            if (btn) { btn.classList.add("is-busy"); setTimeout(function () { btn.disabled = true; }, 0); }
        });
    });

    // ---------------- Add / Edit ----------------
    var editor = document.getElementById("cfEditor");
    var fId = editor.querySelector("[data-cf-id]");
    var fQ = editor.querySelector("#Faq_Question");
    var fA = editor.querySelector("#Faq_Answer");
    var fK = editor.querySelector("[data-cf-keywords]");
    var fActive = editor.querySelector("[data-cf-active]");
    var title = editor.querySelector("[data-cf-title]");
    var saveText = editor.querySelector("[data-cf-save]");

    function clearErrors() {
        editor.querySelectorAll(".uu-field.bad").forEach(function (f) { f.classList.remove("bad"); });
        editor.querySelectorAll(".uu-err").forEach(function (e) { e.textContent = ""; });
        var alert = editor.querySelector(".uu-alert");
        if (alert) { alert.remove(); }
    }

    function fillEditor(data) {
        clearErrors();
        fId.value = data.id || "0";
        fQ.value = data.question || "";
        fA.value = data.answer || "";
        fK.value = data.keywords || "";
        fActive.checked = data.active !== false;
        var isNew = !data.id || data.id === "0";
        title.textContent = isNew ? title.dataset.new : title.dataset.edit;
        saveText.textContent = isNew ? "Add FAQ" : "Save changes";
        updateCounts();
        updatePreview();
    }

    document.querySelectorAll("[data-cf-new]").forEach(function (btn) {
        btn.addEventListener("click", function () { fillEditor({}); openModal(editor, fQ); });
    });

    page.addEventListener("click", function (e) {
        var edit = e.target.closest("[data-cf-edit]");
        if (edit) {
            fillEditor({
                id: edit.dataset.id,
                question: edit.dataset.question,
                answer: edit.dataset.answer,
                keywords: edit.dataset.keywords,
                active: edit.dataset.active === "true"
            });
            openModal(editor, fQ);
            return;
        }
        var del = e.target.closest("[data-cf-delete]");
        if (del) {
            var modal = document.getElementById("cfDelete");
            modal.querySelector("[data-cf-del-id]").value = del.dataset.id;
            modal.querySelector("[data-cf-del-q]").textContent = del.dataset.question;
            openModal(modal, modal.querySelector("[data-uu-submit]"));
        }
    });

    // The server found a problem: the pop-up comes back open with what was typed.
    if (editor.getAttribute("data-cf-start-open") === "true") {
        openModal(editor, editor.querySelector(".uu-field.bad input, .uu-field.bad textarea") || fQ);
    }

    editor.querySelectorAll(".uu-field").forEach(function (field) {
        field.addEventListener("input", function () {
            field.classList.remove("bad");
            var err = field.querySelector(".uu-err");
            if (err) { err.textContent = ""; }
        });
    });

    // character counters ("42 / 200")
    function updateCounts() {
        editor.querySelectorAll("[data-cf-count]").forEach(function (el) {
            var max = parseInt(el.getAttribute("data-cf-count"), 10);
            var hint = el.parentElement.querySelector(".cf-count");
            if (hint) { hint.textContent = el.value.length + " / " + max; }
        });
    }
    editor.querySelectorAll("[data-cf-count]").forEach(function (el) { el.addEventListener("input", updateCounts); });

    // keyword tags preview
    var preview = editor.querySelector("[data-cf-preview]");
    function updatePreview() {
        var seen = {};
        preview.innerHTML = "";
        fK.value.split(/[,，;]/).forEach(function (k) {
            k = k.trim().toLowerCase();
            if (!k || seen[k]) { return; }
            seen[k] = true;
            var span = document.createElement("span");
            span.textContent = k;
            preview.appendChild(span);
        });
    }
    fK.addEventListener("input", updatePreview);
    updateCounts();
    updatePreview();

    // ---------------- show / hide switch: the button submits its own small form ----------------
    document.querySelectorAll(".cf-switch-form").forEach(function (form) {
        form.addEventListener("submit", function () {
            var sw = form.querySelector(".cf-switch");
            sw.classList.toggle("is-on");   // move straight away; the page reloads with the saved state
            sw.disabled = true;
        });
    });

    // ---------------- Try the assistant ----------------
    var tryForm = document.querySelector("[data-cf-try]");
    if (tryForm) {
        var out = document.querySelector("[data-cf-out]");
        var tryInput = tryForm.querySelector("input[type=text]");
        var tryBtn = tryForm.querySelector("button");
        var token = (tryForm.querySelector("input[name='__RequestVerificationToken']") || {}).value || "";

        tryForm.addEventListener("submit", function (e) {
            e.preventDefault();
            var q = tryInput.value.trim();
            if (!q) { tryInput.focus(); return; }
            tryBtn.disabled = true;
            out.hidden = false;
            out.className = "cf-try-out is-wait";
            out.textContent = "Thinking…";

            fetch(tryForm.dataset.url, {
                method: "POST",
                headers: { "Content-Type": "application/json", "RequestVerificationToken": token },
                body: JSON.stringify({ question: q })
            })
                .then(function (r) { if (!r.ok) { throw new Error(r.status); } return r.json(); })
                .then(function (r) {
                    out.className = "cf-try-out";
                    out.innerHTML = format(r.text);
                    var used = document.createElement("span");
                    used.className = "cf-used";
                    var how = r.source === "ai" ? "Answered by the AI" : r.source === "faq" ? "Matched by FAQ keywords" : "Built-in answer";
                    var secs = typeof r.ms === "number" ? " · " + (r.ms < 100 ? "instant" : (r.ms / 1000).toFixed(1) + " s") : "";
                    used.textContent = how + secs + (r.faq ? " · used FAQ “" + r.faq.question + "”" : "") + (r.hours ? " · shows the opening-hours card" : "") +
                        (r.check ? " · table check: " + r.check : "") + (r.form ? " · shows the booking form" : "");
                    out.appendChild(used);
                })
                .catch(function () {
                    out.className = "cf-try-out";
                    out.textContent = "Couldn't reach the chatbot. Please try again.";
                })
                .then(function () { tryBtn.disabled = false; });
        });
    }

    // Same safe formatting as the customer chat: **bold**, [links](/our-pages), "- " bullets, new lines.
    function format(text) {
        var d = document.createElement("div");
        d.textContent = text || "";
        var s = d.innerHTML;
        s = s.replace(/\*\*([^*]+)\*\*/g, "<b>$1</b>");
        s = s.replace(/\[([^\]]+)\]\((\/[^)\s]*)\)/g, function (m, label, href) {
            return href.indexOf("//") === 0 ? label : '<a href="' + href + '" target="_blank">' + label + "</a>";
        });
        return s.split("\n").map(function (l) {
            return /^\s*[-*•]\s+/.test(l) ? "• " + l.replace(/^\s*[-*•]\s+/, "") : l;
        }).join("<br>");
    }

    // ---------------- the FAQ just added / saved ----------------
    var fresh = document.querySelector("[data-cf-new-row]");
    if (fresh) { fresh.scrollIntoView({ block: "center" }); }

    // Empty search box -> leave it out of the address.
    var filters = document.querySelector("[data-uu-filters]");
    if (filters) {
        filters.addEventListener("submit", function () {
            filters.querySelectorAll("input").forEach(function (el) { if (el.name && !el.value) { el.disabled = true; } });
        });
    }
})();
