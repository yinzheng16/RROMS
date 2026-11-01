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
        if (msg.check) { b.appendChild(availCard(msg.check)); }
        if (msg.form) { b.appendChild(bookingForm(msg.form)); }
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

    // ---------------- US09: table availability ----------------
    var setupPromise = null;   // dates + largest party, loaded once
    function bookingSetup() {
        if (!setupPromise) {
            setupPromise = getJson(root.dataset.booking).catch(function (e) { setupPromise = null; throw e; });
        }
        return setupPromise;
    }

    // The small form inside the chat: date chips, time chips (Lunch / Afternoon / Dinner), guests -> "Check availability".
    // Chips instead of long dropdowns: everything is one tap, and only bookable days and times are shown.
    var MONTHS = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
    var DAYS = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];

    function bookingForm(prefill) {
        prefill = prefill || {};
        var box = document.createElement("div");
        box.className = "yc-book";
        box.innerHTML =
            '<div class="yc-bk-row"><span class="yc-bk-lbl">Date</span>' +
            '<div class="yc-dates-wrap"><button type="button" class="yc-dscroll is-left" data-f="left" aria-label="Earlier dates"><i class="bi bi-chevron-left"></i></button>' +
            '<div class="yc-dates" data-f="dates" role="radiogroup" aria-label="Date"><span class="yc-bk-wait">Loading dates…</span></div>' +
            '<button type="button" class="yc-dscroll is-right" data-f="right" aria-label="Later dates"><i class="bi bi-chevron-right"></i></button></div></div>' +
            '<div class="yc-bk-row"><span class="yc-bk-lbl">Time</span><div class="yc-times" data-f="times" role="radiogroup" aria-label="Time"><span class="yc-bk-wait">Pick a date first</span></div></div>' +
            '<div class="yc-guests"><span class="yc-bk-lbl">Guests</span><div class="yc-step">' +
            '<button type="button" data-f="minus" aria-label="Fewer guests">−</button>' +
            '<output data-f="guests" aria-live="polite">2</output>' +
            '<button type="button" data-f="plus" aria-label="More guests">+</button></div></div>' +
            '<button type="button" class="yc-check" data-f="go" disabled><i class="bi bi-search" aria-hidden="true"></i> <span data-f="go-text">Check availability</span></button>';

        var datesEl = box.querySelector('[data-f="dates"]');
        var timesEl = box.querySelector('[data-f="times"]');
        var out = box.querySelector('[data-f="guests"]');
        var go = box.querySelector('[data-f="go"]');
        var goText = box.querySelector('[data-f="go-text"]');
        var guests = prefill.guests || 2;
        var maxGuests = 20;
        var picked = { date: null, dateLabel: "", time: null, timeLabel: "" };

        function paintGuests() {
            out.textContent = guests;
            box.querySelector('[data-f="minus"]').disabled = guests <= 1;
            box.querySelector('[data-f="plus"]').disabled = guests >= maxGuests;
            paintGo();
        }
        function paintGo() {
            go.disabled = !(picked.date && picked.time);
            goText.textContent = picked.date && picked.time
                ? "Check · " + picked.dateLabel + " · " + picked.timeLabel.replace(":00 ", " ") + " · " + guests + (guests === 1 ? " guest" : " guests")
                : "Check availability";
        }
        function select(container, btn) {
            container.querySelectorAll(".is-on").forEach(function (b) { b.classList.remove("is-on"); b.setAttribute("aria-checked", "false"); });
            btn.classList.add("is-on");
            btn.setAttribute("aria-checked", "true");
        }

        // "2026-11-07" -> { top: "Sat" / "Today", day: "7", month: "Nov", label: "Sat 7 Nov" }
        function dateParts(value) {
            var p = value.split("-");
            var d = new Date(+p[0], +p[1] - 1, +p[2]);
            var today = new Date(); today.setHours(0, 0, 0, 0);
            var diff = Math.round((d - today) / 86400000);
            var top = diff === 0 ? "Today" : diff === 1 ? "Tmr" : DAYS[d.getDay()];
            var label = diff === 0 ? "Today" : diff === 1 ? "Tomorrow" : DAYS[d.getDay()] + " " + d.getDate() + " " + MONTHS[d.getMonth()];
            return { top: top, day: String(d.getDate()), month: MONTHS[d.getMonth()], label: label, weekend: d.getDay() === 0 || d.getDay() === 6 };
        }

        function pickDate(btn) {
            select(datesEl, btn);
            picked.date = btn.dataset.value;
            picked.dateLabel = btn.dataset.label;
            picked.time = null;
            btn.scrollIntoView({ block: "nearest", inline: "nearest" });
            loadTimes();
        }

        function loadTimes() {
            paintGo();
            timesEl.innerHTML = '<span class="yc-bk-wait">Loading times…</span>';
            getJson(root.dataset.times + "&date=" + encodeURIComponent(picked.date))
                .then(function (times) {
                    timesEl.innerHTML = "";
                    if (!times.length) { timesEl.innerHTML = '<span class="yc-bk-wait">No times left on this day. Try another date.</span>'; return; }

                    // group: Lunch (before 3 PM), Afternoon (3–5 PM), Dinner (5 PM on)
                    var groups = [["Lunch", []], ["Afternoon", []], ["Dinner", []]];
                    times.forEach(function (t) {
                        var h = parseInt(t.time.split(":")[0], 10);
                        groups[h < 15 ? 0 : h < 17 ? 1 : 2][1].push(t);
                    });
                    var want = prefill.time || "19:00", wantBtn = null, firstBtn = null;
                    groups.forEach(function (g) {
                        if (!g[1].length) { return; }
                        var head = document.createElement("div");
                        head.className = "yc-tgroup";
                        head.textContent = g[0];
                        timesEl.appendChild(head);
                        var row = document.createElement("div");
                        row.className = "yc-trow";
                        g[1].forEach(function (t) {
                            var b = document.createElement("button");
                            b.type = "button";
                            b.className = "yc-chip";
                            b.setAttribute("role", "radio");
                            b.setAttribute("aria-checked", "false");
                            b.dataset.value = t.time;
                            b.textContent = t.label.replace(":00 ", " ").replace(" ", " ");
                            b.addEventListener("click", function () {
                                select(timesEl, b);
                                picked.time = t.time;
                                picked.timeLabel = t.label;
                                paintGo();
                            });
                            row.appendChild(b);
                            if (!firstBtn) { firstBtn = b; }
                            if (t.time === want) { wantBtn = b; }
                        });
                        timesEl.appendChild(row);
                    });
                    // pre-select 7:00 PM (or the asked time) when offered
                    var pre = wantBtn || null;
                    if (pre) {
                        pre.click();
                        // show the chosen time with its group heading above it
                        var head = pre.parentElement.previousElementSibling;
                        timesEl.scrollTop = Math.max(0, head.offsetTop - 2);
                    }
                })
                .catch(function (e) { failed("times", e); });
        }

        // Couldn't load: say so, and offer the booking page instead.
        function failed(what, e) {
            (what === "dates" ? datesEl : timesEl).innerHTML = '<span class="yc-bk-wait">Couldn\'t load ' + what + "</span>";
            if (!box.querySelector(".yc-book-err")) {
                var p = document.createElement("p");
                p.className = "yc-book-err";
                p.innerHTML = "Sorry, I couldn't load the " + what + (e && e.message ? " (" + esc(e.message) + ")" : "") +
                    '. You can still <a href="' + root.dataset.book + '">book on our booking page</a>.';
                box.appendChild(p);
            }
        }

        box.querySelector('[data-f="minus"]').addEventListener("click", function () { if (guests > 1) { guests--; paintGuests(); } });
        box.querySelector('[data-f="plus"]').addEventListener("click", function () { if (guests < maxGuests) { guests++; paintGuests(); } });
        box.querySelector('[data-f="left"]').addEventListener("click", function () { datesEl.scrollBy({ left: -datesEl.clientWidth * .8, behavior: "smooth" }); });
        box.querySelector('[data-f="right"]').addEventListener("click", function () { datesEl.scrollBy({ left: datesEl.clientWidth * .8, behavior: "smooth" }); });
        go.addEventListener("click", function () {
            if (!picked.date || !picked.time) { return; }
            runCheck(picked.date, picked.time, guests, "Table for " + guests + " · " + picked.dateLabel + " · " + picked.timeLabel);
        });

        bookingSetup()
            .then(function (s) {
                maxGuests = s.maxGuests || 20;
                if (guests > maxGuests) { guests = maxGuests; }
                datesEl.innerHTML = "";
                if (!s.dates.length) { datesEl.innerHTML = '<span class="yc-bk-wait">No dates open for booking</span>'; return; }
                var first = null, wanted = null;
                s.dates.forEach(function (d) {
                    var p = dateParts(d.value);
                    var b = document.createElement("button");
                    b.type = "button";
                    b.className = "yc-day" + (p.weekend ? " is-weekend" : "");
                    b.setAttribute("role", "radio");
                    b.setAttribute("aria-checked", "false");
                    b.setAttribute("aria-label", p.label);
                    b.dataset.value = d.value;
                    b.dataset.label = p.label;
                    b.innerHTML = "<small>" + p.top + "</small><b>" + p.day + "</b><small>" + p.month + "</small>";
                    b.addEventListener("click", function () { pickDate(b); });
                    datesEl.appendChild(b);
                    if (!first) { first = b; }
                    if (prefill.date === d.value) { wanted = b; }
                });
                paintGuests();
                pickDate(wanted || first);
            })
            .catch(function (e) { failed("dates", e); });

        paintGuests();
        return box;
    }

    function runCheck(date, time, guests, shown) {
        if (busy) { return; }
        add({ role: "user", text: shown || ("Table for " + guests + " · " + date + " · " + time) });
        setBusy(true);
        getJson(root.dataset.check + "&date=" + encodeURIComponent(date) + "&time=" + encodeURIComponent(time) + "&guests=" + guests)
            .then(function (c) {
                setBusy(false);
                add({
                    role: "bot",
                    text: c.available ? "Good news! 🎉" : (c.valid ? "Sorry, that time is fully booked." : "Sorry, we can't take a booking then."),
                    check: c
                });
            })
            .catch(function () { setBusy(false); add({ role: "bot", text: "Sorry, I couldn't check just now. Please try again." }); });
    }

    // Result card: available (+ table types + "Book this table") or not (+ other times to try).
    function availCard(c) {
        var box = document.createElement("div");
        box.className = "yc-avail " + (c.available ? "is-ok" : "is-no");

        var head = document.createElement("b");
        head.innerHTML = c.available
            ? '<i class="bi bi-check-circle-fill" aria-hidden="true"></i> Table available'
            : '<i class="bi bi-x-circle-fill" aria-hidden="true"></i> ' + (c.valid ? "Fully booked" : "Not available");
        box.appendChild(head);

        if (c.when) {
            var when = document.createElement("p");
            when.textContent = c.when + " · " + c.guests + (c.guests === 1 ? " guest" : " guests");
            box.appendChild(when);
        }
        if (!c.available && c.message) {
            var why = document.createElement("p");
            why.className = "yc-why";
            why.textContent = c.message;
            box.appendChild(why);
        }

        if (c.available && c.types && c.types.length) {
            var ul = document.createElement("ul");
            c.types.slice(0, 4).forEach(function (t) {
                var li = document.createElement("li");
                li.innerHTML = "<span>" + esc(t.name) + "</span><small>" +
                    (t.minimumSpend > 0 ? "min. spend RM " + Number(t.minimumSpend).toFixed(0) : (t.tablesLeft === 1 ? "1 left" : t.tablesLeft + " left")) + "</small>";
                ul.appendChild(li);
            });
            box.appendChild(ul);
        }

        if (c.available && c.bookUrl) {
            var a = document.createElement("a");
            a.className = "yc-go";
            a.href = c.bookUrl;
            a.innerHTML = 'Book this table <i class="bi bi-arrow-right" aria-hidden="true"></i>';
            box.appendChild(a);
            var note = document.createElement("div");
            note.className = "yc-src";
            note.textContent = root.dataset.signedIn === "true"
                ? "You choose the table type and confirm on the booking page."
                : "You'll log in, then choose the table type and confirm.";
            box.appendChild(note);
        }

        if (!c.available && c.suggestions && c.suggestions.length) {
            var alt = document.createElement("div");
            alt.className = "yc-alt";
            var label = document.createElement("span");
            label.textContent = "Free times nearby:";
            alt.appendChild(label);
            c.suggestions.forEach(function (s) {
                var btn = document.createElement("button");
                btn.type = "button";
                btn.textContent = s.label;
                btn.addEventListener("click", function () {
                    runCheck(s.date, s.time, c.guests, "Table for " + c.guests + " · " + s.label);
                });
                alt.appendChild(btn);
            });
            box.appendChild(alt);
        }

        if (!c.available) {
            var again = document.createElement("button");
            again.type = "button";
            again.className = "yc-again";
            again.textContent = "Pick another date or time";
            again.addEventListener("click", function () {
                add({ role: "bot", text: "Pick another date, time or number of guests:", form: { date: c.date, guests: c.guests } });
            });
            box.appendChild(again);
        }
        return box;
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
                    check: reply.check || null,
                    form: reply.bookingForm ? {} : null,
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
            .then(function (r) {
                var type = r.headers.get("content-type") || "";
                if (!r.ok || type.indexOf("json") === -1) {
                    // shown in the browser console (F12) to help find the problem
                    console.warn("YORU chat: " + url + " answered " + r.status + " (" + type + ")");
                    throw new Error(r.ok ? "not json" : String(r.status));
                }
                return r.json();
            });
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
                add({ role: "bot", text: "Let's find you a table! Pick a date, time and number of guests:", form: {} });
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
