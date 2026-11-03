// =========================================================
// US44 Filtered reports (Member 5)
// Pages/Admin/Reports
// - Filters apply as soon as they change; the page updates in the
//   background (no reload, no jump). The address still changes, so
//   Back, Refresh and bookmarks work. Without JavaScript the filters
//   are a normal form and links.
// - Styled drop-down menus are drawn over the normal <select>s.
// - Custom date range: a styled calendar is drawn over the two date boxes.
// - PDF version: opens the print window, where "Save as PDF" is chosen.
// =========================================================
(function () {
    "use strict";

    // ---------- PDF version ----------
    if (document.body.classList.contains("rp-print-body")) {
        document.addEventListener("click", function (event) {
            if (event.target.closest("[data-rp-print]")) {
                window.print();
            }
        });
        window.addEventListener("load", function () {
            setTimeout(function () { window.print(); }, 400);
        });
        return;
    }

    var page = document.querySelector("[data-rp]");
    if (!page) {
        return;
    }

    var loading = false;

    // ---------- Styled drop-downs ----------
    var CHEVRON = '<svg class="rp-chev" width="11" height="7" viewBox="0 0 11 7" aria-hidden="true">' +
        '<path d="M1 1l4.5 4.5L10 1" fill="none" stroke="currentColor" stroke-width="1.6"/></svg>';

    function escapeHtml(text) {
        var div = document.createElement("div");
        div.textContent = text;
        return div.innerHTML;
    }

    function dot(option) {
        var color = option && option.getAttribute("data-color");
        return color ? '<i style="background:' + escapeHtml(color) + '"></i>' : "";
    }

    function closeMenus(except) {
        page.querySelectorAll(".rp-dd.is-open").forEach(function (dd) {
            if (dd !== except) {
                dd.classList.remove("is-open");
                var b = dd.querySelector(".rp-ddb");
                if (b) { b.setAttribute("aria-expanded", "false"); }
            }
        });
    }

    function buildDropdown(select) {
        var dd = select.parentNode;
        dd.querySelectorAll(".rp-ddb, .rp-ddm").forEach(function (old) { old.remove(); });
        var current = select.options[select.selectedIndex];
        var label = select.getAttribute("aria-label") || "";
        var kanji = select.getAttribute("data-rp-kanji") || "";

        select.classList.add("rp-sr");
        select.tabIndex = -1;

        var button = document.createElement("button");
        button.type = "button";
        button.className = "rp-ddb" + (select.classList.contains("is-set") ? " is-set" : "");
        button.setAttribute("aria-haspopup", "listbox");
        button.setAttribute("aria-expanded", "false");
        button.setAttribute("aria-label", label + ": " + (current ? current.text : ""));
        button.innerHTML = dot(current) + "<span>" + escapeHtml(current ? current.text : "") + "</span>" + CHEVRON;

        var menu = document.createElement("div");
        menu.className = "rp-ddm" +
            (dd.classList.contains("rp-dd-cal") || dd.classList.contains("rp-dd-up") ? " is-right" : "") +
            (dd.classList.contains("rp-dd-up") ? " is-up" : "");
        menu.setAttribute("role", "listbox");
        menu.setAttribute("aria-label", label);

        var html = '<div class="rp-ddm-hd"><b lang="ja">' + escapeHtml(kanji) + "</b>" + escapeHtml(label) + "</div>";
        Array.prototype.forEach.call(select.options, function (option) {
            if (option.value === "custom") {
                html += '<div class="rp-ddm-sep"></div>';
            }
            html += '<button type="button" role="option" aria-selected="' + option.selected + '" data-value="' +
                escapeHtml(option.value) + '" class="' + (option.selected ? "is-on" : "") + '">' +
                dot(option) + "<span>" + escapeHtml(option.text) + '</span><span class="rp-ck" aria-hidden="true">✓</span></button>';
        });
        menu.innerHTML = html;

        dd.appendChild(button);
        dd.appendChild(menu);

        button.addEventListener("click", function () {
            var open = !dd.classList.contains("is-open");
            closeMenus(dd);
            dd.classList.toggle("is-open", open);
            button.setAttribute("aria-expanded", open ? "true" : "false");
            if (open) {
                var on = menu.querySelector(".is-on") || menu.querySelector("button");
                if (on) { on.focus(); }
            }
        });

        button.addEventListener("keydown", function (event) {
            if (event.key === "ArrowDown") {
                event.preventDefault();
                button.click();
            }
        });

        menu.addEventListener("click", function (event) {
            var item = event.target.closest("[data-value]");
            if (!item) {
                return;
            }
            closeMenus();
            button.focus();
            if (select.value !== item.getAttribute("data-value")) {
                select.value = item.getAttribute("data-value");
                select.dispatchEvent(new Event("change", { bubbles: true }));
            }
        });

        menu.addEventListener("keydown", function (event) {
            var items = Array.prototype.slice.call(menu.querySelectorAll("button"));
            var i = items.indexOf(document.activeElement);
            if (event.key === "ArrowDown") {
                event.preventDefault();
                items[Math.min(items.length - 1, i + 1)].focus();
            } else if (event.key === "ArrowUp") {
                event.preventDefault();
                items[Math.max(0, i - 1)].focus();
            } else if (event.key === "Escape") {
                closeMenus();
                button.focus();
            }
        });
    }

    // ---------- Calendar for "Custom range…" ----------
    var MONTHS = ["January", "February", "March", "April", "May", "June", "July",
        "August", "September", "October", "November", "December"];
    var SHORT = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    function parseDate(text) {
        var p = (text || "").split("-");
        return p.length === 3 ? new Date(+p[0], +p[1] - 1, +p[2]) : null;
    }

    function isoDate(d) {
        return d.getFullYear() + "-" + String(d.getMonth() + 1).padStart(2, "0") + "-" + String(d.getDate()).padStart(2, "0");
    }

    function niceDate(text, withYear) {
        var d = parseDate(text);
        return d ? d.getDate() + " " + SHORT[d.getMonth()] + (withYear ? " " + d.getFullYear() : "") : "";
    }

    function buildCalendar(form) {
        var fromInput = form.querySelector("input[name=from]");
        var toInput = form.querySelector("input[name=to]");
        if (!fromInput || !toInput) {
            return;
        }
        form.querySelectorAll(".rp-range-btn, .rp-cal").forEach(function (old) { old.remove(); });
        form.classList.add("is-enhanced");

        var max = fromInput.getAttribute("max");
        var state = { start: fromInput.value, end: toInput.value, view: parseDate(toInput.value) || new Date() };
        state.view = new Date(state.view.getFullYear(), state.view.getMonth(), 1);

        var button = document.createElement("button");
        button.type = "button";
        button.className = "rp-range-btn";
        button.setAttribute("aria-haspopup", "dialog");
        function buttonText() {
            button.innerHTML = '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true">' +
                '<rect x="3" y="5" width="18" height="16" rx="2"/><path d="M3 10h18M8 3v4M16 3v4"/></svg><span>' +
                niceDate(fromInput.value) + " – " + niceDate(toInput.value, true) + "</span>";
        }
        buttonText();

        var cal = document.createElement("div");
        cal.className = "rp-cal";
        cal.setAttribute("role", "dialog");
        cal.setAttribute("aria-label", "Choose a date range");

        function draw() {
            var y = state.view.getFullYear(), m = state.view.getMonth();
            var first = new Date(y, m, 1);
            var shift = (first.getDay() + 6) % 7; // Monday first
            var html = '<div class="rp-ddm-hd"><b lang="ja">期間</b>Custom range</div>' +
                '<div class="rp-cal-nav"><button type="button" data-cal-move="-1" aria-label="Previous month">‹</button>' +
                "<span>" + MONTHS[m] + " " + y + "</span>" +
                '<button type="button" data-cal-move="1" aria-label="Next month"' + (max && isoDate(new Date(y, m + 1, 1)) > max ? " disabled" : "") + ">›</button></div>" +
                '<div class="rp-cal-grid">';
            ["Mo", "Tu", "We", "Th", "Fr", "Sa", "Su"].forEach(function (w) { html += "<i>" + w + "</i>"; });
            for (var k = 0; k < 42; k++) {
                var d = new Date(y, m, 1 - shift + k);
                var iso = isoDate(d);
                var cls = [];
                if (d.getMonth() !== m) { cls.push("is-out"); }
                if (iso === max) { cls.push("is-today"); }
                if (iso === state.start) { cls.push("is-start"); }
                if (iso === state.end) { cls.push("is-end"); }
                if (state.start && state.end && iso > state.start && iso < state.end) { cls.push("is-in"); }
                var off = max && iso > max;
                html += '<button type="button" data-cal-day="' + iso + '" class="' + cls.join(" ") + '"' + (off ? " disabled" : "") + ">" + d.getDate() + "</button>";
                if (k === 34 && new Date(y, m, 1 - shift + 35).getMonth() !== m) { break; }
            }
            html += "</div>" +
                '<div class="rp-cal-ft"><span>' + (state.start ? niceDate(state.start) : "Pick a start") + " → " +
                (state.end ? niceDate(state.end, true) : "pick an end") + "</span>" +
                '<button type="button" class="rp-cal-cancel" data-cal-cancel>Cancel</button>' +
                '<button type="button" class="rp-cal-apply" data-cal-apply' + (state.start ? "" : " disabled") + ">Apply</button></div>";
            cal.innerHTML = html;
        }

        function open() {
            state.start = fromInput.value;
            state.end = toInput.value;
            draw();
            form.classList.add("is-open");
            button.setAttribute("aria-expanded", "true");
        }

        function close() {
            form.classList.remove("is-open");
            button.setAttribute("aria-expanded", "false");
        }

        button.addEventListener("click", function () {
            if (form.classList.contains("is-open")) { close(); } else { closeMenus(); open(); }
        });

        cal.addEventListener("click", function (event) {
            event.stopPropagation();
            var move = event.target.closest("[data-cal-move]");
            var day = event.target.closest("[data-cal-day]");
            if (move) {
                state.view = new Date(state.view.getFullYear(), state.view.getMonth() + (+move.getAttribute("data-cal-move")), 1);
                draw();
            } else if (day) {
                var iso = day.getAttribute("data-cal-day");
                if (!state.start || state.end) {
                    state.start = iso;
                    state.end = "";
                } else if (iso < state.start) {
                    state.start = iso;
                } else {
                    state.end = iso;
                }
                draw();
            } else if (event.target.closest("[data-cal-cancel]")) {
                close();
                button.focus();
            } else if (event.target.closest("[data-cal-apply]")) {
                fromInput.value = state.start;
                toInput.value = state.end || state.start;
                close();
                buttonText();
                load(formUrl(form), true);
            }
        });

        form.appendChild(button);
        form.appendChild(cal);
        form.rpOpen = open;
        form.rpClose = close;
    }

    function setUp() {
        page.querySelectorAll(".rp-dd > select").forEach(buildDropdown);
        page.querySelectorAll("[data-rp-custom]").forEach(buildCalendar);
    }

    document.addEventListener("click", function (event) {
        if (!event.target.closest(".rp-dd")) {
            closeMenus();
        }
        if (!event.target.closest("[data-rp-custom]")) {
            page.querySelectorAll("[data-rp-custom].is-open").forEach(function (f) { f.rpClose(); });
        }
    });

    document.addEventListener("keydown", function (event) {
        if (event.key === "Escape") {
            closeMenus();
            page.querySelectorAll("[data-rp-custom].is-open").forEach(function (f) { f.rpClose(); });
        }
    });

    // ---------- Background loading ----------
    // If the admin changes something while a report is still loading, remember only
    // the newest request and run it as soon as the current one finishes.
    var queued = null;

    function load(url, push) {
        if (loading) {
            queued = { url: url, push: push };
            return;
        }
        loading = true;
        var slow = setTimeout(function () { page.classList.add("is-loading"); }, 350);

        var searchFocused = document.activeElement && document.activeElement.hasAttribute("data-rp-search");
        // What the search box said when this request started. Only text typed AFTER
        // this point is kept; otherwise the new report's own search wins (e.g. Clear filters).
        var startInput = page.querySelector("[data-rp-search]");
        var typedAtStart = startInput ? startInput.value : null;

        fetch(url, { credentials: "same-origin" })
            .then(function (response) {
                if (!response.ok) { throw new Error("HTTP " + response.status); }
                return response.text();
            })
            .then(function (html) {
                var doc = new DOMParser().parseFromString(html, "text/html");
                var fresh = doc.querySelector("[data-rp]");
                if (!fresh) { throw new Error("No report in the response"); }

                // Keep what the admin is typing right now, even if they typed more
                // while this report was loading.
                var oldInput = page.querySelector("[data-rp-search]");
                var typed = oldInput ? oldInput.value : null;
                var caret = oldInput && searchFocused ? oldInput.selectionStart : 0;

                page.innerHTML = fresh.innerHTML;
                if (push) {
                    history.pushState({ rp: true }, "", url);
                }
                setUp();

                var input = page.querySelector("[data-rp-search]");
                if (input && typed !== null && typed !== typedAtStart && input.value !== typed) {
                    input.value = typed;
                    if (!queued) {
                        queued = { url: formUrl(input.form), push: true };
                    }
                }
                if (input && searchFocused) {
                    input.focus();
                    input.setSelectionRange(caret, caret);
                }
            })
            .catch(function () {
                window.location.href = url;
            })
            .finally(function () {
                clearTimeout(slow);
                loading = false;
                page.classList.remove("is-loading");
                if (queued) {
                    var next = queued;
                    queued = null;
                    load(next.url, next.push);
                }
            });
    }

    function formUrl(form) {
        var params = new URLSearchParams();
        new FormData(form).forEach(function (value, key) {
            if (value !== "") { params.append(key, value); }
        });
        var query = params.toString();
        return "/Admin/Reports" + (query ? "?" + query : "");
    }

    page.addEventListener("click", function (event) {
        // "+N more" under the status / category bar
        var more = event.target.closest("[data-rp-more]");
        if (more) {
            var list = more.parentNode;
            var all = list.classList.toggle("is-all");
            more.textContent = more.getAttribute(all ? "data-less" : "data-more");
            more.setAttribute("aria-expanded", all ? "true" : "false");
            return;
        }

        var link = event.target.closest("a");
        if (!link || !page.contains(link)) {
            return;
        }
        var href = link.getAttribute("href") || "";
        var plain = link.hasAttribute("data-rp-plain") || link.target === "_blank";
        var modified = event.metaKey || event.ctrlKey || event.shiftKey || event.altKey;
        if (!plain && !modified && href.indexOf("/Admin/Reports") === 0) {
            event.preventDefault();
            load(href, true);
        }
    });

    page.addEventListener("submit", function (event) {
        var form = event.target.closest("[data-rp-form]");
        if (!form) {
            return;
        }
        event.preventDefault();
        load(formUrl(form), true);
    });

    page.addEventListener("change", function (event) {
        var field = event.target;

        // Date range: a preset loads straight away; "Custom range…" shows the two dates.
        if (field.hasAttribute("data-rp-period")) {
            if (field.value === "custom") {
                buildDropdown(field);
                var custom = page.querySelector("[data-rp-custom]");
                if (custom) {
                    custom.hidden = false;
                    if (custom.rpOpen) {
                        setTimeout(custom.rpOpen, 0);
                    } else {
                        var from = custom.querySelector("input[type=date]");
                        if (from) { from.focus(); }
                    }
                }
            } else {
                load(field.value, true);
            }
            return;
        }

        if (!field.hasAttribute("data-rp-auto")) {
            return;
        }
        if (field.type === "date" && !field.value) {
            return;
        }
        load(formUrl(field.form), true);
    });

    // Customer search: wait until typing stops.
    var timer = null;
    page.addEventListener("input", function (event) {
        if (!event.target.hasAttribute("data-rp-search")) {
            return;
        }
        clearTimeout(timer);
        var form = event.target.form;
        timer = setTimeout(function () { load(formUrl(form), true); }, 450);
    });

    window.addEventListener("popstate", function () {
        load(window.location.pathname + window.location.search, false);
    });

    setUp();
})();