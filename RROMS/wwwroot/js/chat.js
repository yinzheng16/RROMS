// US08 (Owner: Member 1): "Ask YORU" chat bubble on the customer pages.
// Talks to Pages/Chat.cshtml. The conversation is kept for this browser tab only (sessionStorage),
// so it stays while the customer moves between pages and is gone when the tab is closed.
(function () {
    "use strict";

    var root = document.getElementById("yoruChat");
    if (!root) { return; }

    var panel = root.querySelector(".yc-panel");
    var list = root.querySelector(".yc-msgs");
    var form = root.querySelector("[data-yc-form]");
    var input = form.querySelector("input");
    var sendBtn = form.querySelector("button");
    var toggle = root.querySelector("[data-yc-toggle]");
    var token = (root.querySelector("input[name='__RequestVerificationToken']") || {}).value || "";
    var mon = root.querySelector(".yc-av img").getAttribute("src");

    var KEY = "yoru.chat.v1";
    var state = load();
    var busy = false;

    // ---------------- saving the chat for this tab ----------------
    function load() {
        try {
            var s = JSON.parse(sessionStorage.getItem(KEY) || "null");
            if (s && Array.isArray(s.msgs)) { return s; }
        } catch (e) { /* private mode or blocked: start fresh */ }
        return { open: false, seen: false, msgs: [] };
    }
    function save() {
        try {
            state.msgs = state.msgs.slice(-40);
            sessionStorage.setItem(KEY, JSON.stringify(state));
        } catch (e) { /* ignore */ }
    }

    // ---------------- open / close ----------------
    function setOpen(open, focus) {
        state.open = open;
        if (open) { state.seen = true; }
        panel.hidden = !open;
        root.classList.toggle("is-open", open);
        root.classList.toggle("is-seen", state.seen);
        toggle.setAttribute("aria-expanded", open ? "true" : "false");
        if (open) {
            if (state.msgs.length === 0) { welcome(); }
            scrollDown();
            if (focus) { setTimeout(function () { input.focus(); }, 60); }
        }
        save();
    }

    toggle.addEventListener("click", function () { setOpen(panel.hidden, true); });
    root.querySelector("[data-yc-close]").addEventListener("click", function () { setOpen(false); toggle.focus(); });
    root.querySelector("[data-yc-reset]").addEventListener("click", function () {
        state.msgs = [];
        list.innerHTML = "";
        welcome();
        input.focus();
    });
    document.addEventListener("keydown", function (e) {
        if (e.key === "Escape" && !panel.hidden) { setOpen(false); toggle.focus(); }
    });

    function welcome() {
        add({ role: "bot", text: "Konbanwa! 🏮 I'm YORU's assistant. Ask me about our **menu**, **opening hours** or anything about the restaurant, or tap a button below." });
    }

    // ---------------- messages ----------------
    function add(msg) {
        state.msgs.push(msg);
        save();
        list.appendChild(render(msg));
        scrollDown();
    }

    function render(msg) {
        var row = document.createElement("div");
        row.className = "yc-row " + (msg.role === "user" ? "is-me" : "is-bot");

        if (msg.role !== "user") {
            var mini = document.createElement("span");
            mini.className = "yc-mini";
            mini.setAttribute("aria-hidden", "true");
            mini.innerHTML = '<img src="' + mon + '" alt="" />';
            row.appendChild(mini);
        }

        var b = document.createElement("div");
        b.className = "yc-b";
        b.innerHTML = format(msg.text || "");

        if (msg.hours) { b.appendChild(hoursCard(msg.hours)); }
        if (msg.faqs) { b.appendChild(faqList(msg.faqs)); }
        if (msg.link) {
            var a = document.createElement("a");
            a.className = "yc-go";
            a.href = msg.link.href;
            a.innerHTML = esc(msg.link.text) + ' <i class="bi bi-arrow-right" aria-hidden="true"></i>';
            b.appendChild(a);
        }
        if (msg.note) {
            var n = document.createElement("div");
            n.className = "yc-src";
            n.textContent = msg.note;
            b.appendChild(n);
        }

        row.appendChild(b);
        return row;
    }

    function hoursCard(h) {
        var wrap = document.createElement("div");
        var box = document.createElement("div");
        box.className = "yc-hours";
        (h.days || []).forEach(function (d) {
            var r = document.createElement("div");
            if (d.isToday) { r.classList.add("is-today"); }
            if (d.isClosed) { r.classList.add("is-closed"); }
            r.innerHTML = "<span>" + esc(d.day) + (d.isToday ? " · today" : "") + "</span><span>" + esc(d.hours) + "</span>";
            box.appendChild(r);
        });
        wrap.appendChild(box);
        (h.specials || []).forEach(function (s) {
            var p = document.createElement("div");
            p.className = "yc-special";
            p.innerHTML = '<i class="bi bi-calendar-event" aria-hidden="true"></i> ' + esc(s);
            wrap.appendChild(p);
        });
        return wrap;
    }

    function faqList(faqs) {
        var box = document.createElement("div");
        box.className = "yc-faqs";
        faqs.forEach(function (f) {
            var btn = document.createElement("button");
            btn.type = "button";
            btn.innerHTML = "<span>" + esc(f.question) + '</span><i class="bi bi-chevron-right" aria-hidden="true"></i>';
            // The admin's own answer, straight away (no AI call).
            btn.addEventListener("click", function () {
                add({ role: "user", text: f.question });
                add({ role: "bot", text: f.answer });
            });
            box.appendChild(btn);
        });
        return box;
    }

    // Safe formatting: escape everything, then allow **bold**, [links](/our-pages), "- " bullets and new lines.
    function format(text) {
        var s = esc(text);
        s = s.replace(/\*\*([^*]+)\*\*/g, "<b>$1</b>");
        s = s.replace(/\[([^\]]+)\]\((\/[^)\s]*)\)/g, function (m, label, href) {
            if (href.indexOf("//") === 0) { return label; }   // only our own pages
            return '<a href="' + href + '">' + label + "</a>";
        });
        s = s.split("\n").map(function (line) {
            return /^\s*[-*•]\s+/.test(line) ? "• " + line.replace(/^\s*[-*•]\s+/, "") : line;
        }).join("<br>");
        return s;
    }

    function esc(s) {
        var d = document.createElement("div");
        d.textContent = s == null ? "" : String(s);
        return d.innerHTML.replace(/"/g, "&quot;");
    }

    function scrollDown() { list.scrollTop = list.scrollHeight; }

    function setBusy(on) {
        busy = on;
        sendBtn.disabled = on;
        root.querySelectorAll("[data-yc-quick]").forEach(function (b) { b.disabled = on; });
        var t = list.querySelector(".yc-typing-row");
        if (on && !t) {
            t = document.createElement("div");
            t.className = "yc-row is-bot yc-typing-row";
            t.innerHTML = '<span class="yc-mini" aria-hidden="true"><img src="' + mon + '" alt="" /></span><div class="yc-b yc-typing" aria-label="Typing"><i></i><i></i><i></i></div>';
            list.appendChild(t);
            scrollDown();
        } else if (!on && t) {
            t.remove();
        }
    }

    // ---------------- asking ----------------
    function ask(question, shown, alone) {
        question = (question || "").trim();
        if (!question || busy) { return; }

        // the last few messages give the AI the context ("and on Sunday?")
        var history = alone ? [] : state.msgs
            .filter(function (m) { return m.text && (m.role === "user" || m.role === "bot"); })
            .slice(-6)
            .map(function (m) { return { role: m.role, text: m.text }; });

        add({ role: "user", text: shown || question });
        setBusy(true);

        fetch(root.dataset.ask, {
            method: "POST",
            headers: { "Content-Type": "application/json", "RequestVerificationToken": token },
            body: JSON.stringify({ question: question, history: history })
        })
            .then(function (r) { if (!r.ok) { throw new Error(r.status); } return r.json(); })
            .then(function (reply) {
                setBusy(false);
                add({
                    role: "bot",
                    text: reply.text,
                    hours: reply.hours || null,
                    note: reply.source === "faq" ? "From our FAQs" : null
                });
            })
            .catch(function () {
                setBusy(false);
                add({ role: "bot", text: "Sorry, I couldn't connect just now. Please try again in a moment." });
            });
    }

    form.addEventListener("submit", function (e) {
        e.preventDefault();
        var q = input.value;
        input.value = "";
        ask(q);
    });

    function getJson(url) {
        return fetch(url, { headers: { "Accept": "application/json" } })
            .then(function (r) { if (!r.ok) { throw new Error(r.status); } return r.json(); });
    }

    // ---------------- quick buttons ----------------
    root.querySelectorAll("[data-yc-quick]").forEach(function (btn) {
        btn.addEventListener("click", function () {
            if (busy) { return; }
            var kind = btn.getAttribute("data-yc-quick");

            // sent on its own (no earlier messages), so the answer can be remembered and comes back instantly next time
            if (kind === "popular") { ask("What are your most popular dishes?", "Popular dishes", true); return; }

            if (kind === "book") {
                add({ role: "user", text: "Book a table" });
                add({
                    role: "bot",
                    text: root.dataset.signedIn === "true"
                        ? "Choose a date, time and number of guests on our booking page."
                        : "Choose a date, time and number of guests on our booking page. You'll be asked to log in first.",
                    link: { href: root.dataset.book, text: "Book a table" }
                });
                return;
            }

            if (kind === "hours") {
                add({ role: "user", text: "Opening hours" });
                setBusy(true);
                getJson(root.dataset.hours)
                    .then(function (h) {
                        setBusy(false);
                        add({ role: "bot", text: "Here are our opening hours. **" + h.status + "**.", hours: h });
                    })
                    .catch(function () { setBusy(false); add({ role: "bot", text: "Sorry, I couldn't load the hours just now." }); });
                return;
            }

            if (kind === "faqs") {
                add({ role: "user", text: "FAQs" });
                setBusy(true);
                getJson(root.dataset.faqs)
                    .then(function (faqs) {
                        setBusy(false);
                        if (!faqs.length) { add({ role: "bot", text: "There are no FAQs yet. Just type your question!" }); return; }
                        add({ role: "bot", text: "Here are the questions guests often ask. Tap one:", faqs: faqs });
                    })
                    .catch(function () { setBusy(false); add({ role: "bot", text: "Sorry, I couldn't load the FAQs just now." }); });
            }
        });
    });

    // ---------------- start ----------------
    state.msgs.forEach(function (m) { list.appendChild(render(m)); });
    root.classList.toggle("is-seen", !!state.seen);
    if (state.open) { setOpen(true, false); }
})();
