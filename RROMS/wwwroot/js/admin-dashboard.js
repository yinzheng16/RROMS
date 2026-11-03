// =========================================================
// US46 Interactive dashboard (Member 5)
// Pages/Admin/Dashboard
// - Draws the charts with Chart.js: line (sales), doughnut (top dishes),
//   stacked bar (reservations). Hover shows exact numbers.
// - Clicking a day opens that day's report.
// - Custom range: the same styled calendar as the Reports page.
// - The date range drop-down loads the same page with ?range=…, so every
//   number and chart updates together. Without JavaScript the drop-down
//   is a normal <select> and the custom range is a normal form.
// =========================================================
(function () {
    "use strict";

    var page = document.querySelector("[data-db]");
    if (!page) {
        return;
    }

    // ---------- Date range drop-down ----------
    var CHEVRON = '<svg width="11" height="7" viewBox="0 0 11 7" aria-hidden="true">' +
        '<path d="M1 1l4.5 4.5L10 1" fill="none" stroke="currentColor" stroke-width="1.6"/></svg>';

    function escapeHtml(text) {
        var div = document.createElement("div");
        div.textContent = text;
        return div.innerHTML;
    }

    function applyRange(select) {
        var custom = page.querySelector("[data-db-custom]");
        if (select.value === "custom") {
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
            window.location.href = select.value;
        }
    }

    var select = page.querySelector("[data-db-period]");
    if (select) {
        // Works without the styled menu too.
        select.addEventListener("change", function () { applyRange(select); });

        var dd = select.parentNode;
        var current = select.options[select.selectedIndex];
        select.classList.add("db-sr");
        select.tabIndex = -1;

        var button = document.createElement("button");
        button.type = "button";
        button.className = "db-ddb";
        button.setAttribute("aria-haspopup", "listbox");
        button.setAttribute("aria-expanded", "false");
        button.setAttribute("aria-label", "Date range: " + current.text);
        button.innerHTML = "<span>" + escapeHtml(current.text) + "</span>" + CHEVRON;

        var menu = document.createElement("div");
        menu.className = "db-ddm";
        menu.setAttribute("role", "listbox");
        var html = '<div class="db-ddm-hd"><b lang="ja">期間</b>DATE RANGE</div>';
        Array.prototype.forEach.call(select.options, function (option) {
            if (option.value === "custom") {
                html += '<div class="db-ddm-sep"></div>';
            }
            html += '<button type="button" role="option" aria-selected="' + option.selected + '" data-value="' +
                escapeHtml(option.value) + '" class="' + (option.selected ? "is-on" : "") + '"><span>' +
                escapeHtml(option.text) + '</span><span class="db-ck" aria-hidden="true">✓</span></button>';
        });
        menu.innerHTML = html;
        dd.appendChild(button);
        dd.appendChild(menu);

        function close() {
            dd.classList.remove("is-open");
            button.setAttribute("aria-expanded", "false");
        }

        button.addEventListener("click", function () {
            var open = !dd.classList.contains("is-open");
            dd.classList.toggle("is-open", open);
            button.setAttribute("aria-expanded", open ? "true" : "false");
            if (open) {
                var on = menu.querySelector(".is-on") || menu.querySelector("button");
                if (on) { on.focus(); }
            }
        });

        menu.addEventListener("click", function (event) {
            var item = event.target.closest("[data-value]");
            if (!item) {
                return;
            }
            close();
            select.value = item.getAttribute("data-value");
            applyRange(select);
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
                close();
                button.focus();
            }
        });

        document.addEventListener("click", function (event) {
            if (!event.target.closest(".db-dd")) { close(); }
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
        form.querySelectorAll(".db-range-btn, .db-cal").forEach(function (old) { old.remove(); });
        form.classList.add("is-enhanced");

        var max = fromInput.getAttribute("max");
        var state = { start: fromInput.value, end: toInput.value, view: parseDate(toInput.value) || new Date() };
        state.view = new Date(state.view.getFullYear(), state.view.getMonth(), 1);

        var button = document.createElement("button");
        button.type = "button";
        button.className = "db-range-btn";
        button.setAttribute("aria-haspopup", "dialog");
        function buttonText() {
            button.innerHTML = '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true">' +
                '<rect x="3" y="5" width="18" height="16" rx="2"/><path d="M3 10h18M8 3v4M16 3v4"/></svg><span>' +
                niceDate(fromInput.value) + " – " + niceDate(toInput.value, true) + "</span>";
        }
        buttonText();

        var cal = document.createElement("div");
        cal.className = "db-cal";
        cal.setAttribute("role", "dialog");
        cal.setAttribute("aria-label", "Choose a date range");

        function draw() {
            var y = state.view.getFullYear(), m = state.view.getMonth();
            var first = new Date(y, m, 1);
            var shift = (first.getDay() + 6) % 7; // Monday first
            var html = '<div class="db-ddm-hd"><b lang="ja">期間</b>Custom range</div>' +
                '<div class="db-cal-nav"><button type="button" data-cal-move="-1" aria-label="Previous month">‹</button>' +
                "<span>" + MONTHS[m] + " " + y + "</span>" +
                '<button type="button" data-cal-move="1" aria-label="Next month"' + (max && isoDate(new Date(y, m + 1, 1)) > max ? " disabled" : "") + ">›</button></div>" +
                '<div class="db-cal-grid">';
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
                '<div class="db-cal-ft"><span>' + (state.start ? niceDate(state.start) : "Pick a start") + " → " +
                (state.end ? niceDate(state.end, true) : "pick an end") + "</span>" +
                '<button type="button" class="db-cal-cancel" data-cal-cancel>Cancel</button>' +
                '<button type="button" class="db-cal-apply" data-cal-apply' + (state.start ? "" : " disabled") + ">Apply</button></div>";
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
            if (form.classList.contains("is-open")) { close(); } else { open(); }
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
                form.submit();
            }
        });

        form.appendChild(button);
        form.appendChild(cal);
        form.rpOpen = open;
        form.rpClose = close;
    }

    page.querySelectorAll("[data-db-custom]").forEach(buildCalendar);

    document.addEventListener("click", function (event) {
        if (!event.target.closest("[data-db-custom]")) {
            page.querySelectorAll("[data-db-custom].is-open").forEach(function (f) { f.rpClose(); });
        }
    });

    document.addEventListener("keydown", function (event) {
        if (event.key === "Escape") {
            page.querySelectorAll("[data-db-custom].is-open").forEach(function (f) { f.rpClose(); });
        }
    });

    // ---------- Charts ----------
    var holder = page.querySelector("[data-db-charts]");
    if (!holder || typeof window.Chart === "undefined") {
        return;   // no data, or Chart.js could not load: the numbers above still show
    }

    var data;
    try {
        data = JSON.parse(holder.getAttribute("data-db-charts"));
    } catch (e) {
        return;
    }

    var Chart = window.Chart;
    var money = new Intl.NumberFormat("en-MY", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    var days = data.days;
    var labels = days.map(function (d) { return d.label; });

    Chart.defaults.font.family = '"Josefin Sans", -apple-system, sans-serif';
    Chart.defaults.color = "#86725F";

    var tooltip = {
        backgroundColor: "#1E1A18",
        titleFont: { family: '"Noto Serif JP", serif', size: 13 },
        bodyFont: { size: 12.5 },
        padding: 10,
        cornerRadius: 10,
        boxPadding: 4
    };
    var grid = { color: "rgba(150, 120, 90, .12)" };

    function goTo(link) {
        if (link) { window.location.href = link; }
    }

    // Line: net sales (or orders) per day, this period vs the earlier one.
    var mode = "net";
    var salesChart = null;
    var salesCanvas = document.getElementById("dbSales");

    function drawSales() {
        if (!salesCanvas) { return; }
        var isNet = mode === "net";
        var ctx = salesCanvas.getContext("2d");
        var fill = ctx.createLinearGradient(0, 0, 0, 280);
        fill.addColorStop(0, "rgba(201, 57, 43, .22)");
        fill.addColorStop(1, "rgba(201, 57, 43, 0)");
        var fmt = function (v) { return isNet ? "RM " + money.format(v) : v + (v === 1 ? " order" : " orders"); };

        if (salesChart) { salesChart.destroy(); }
        salesChart = new Chart(ctx, {
            type: "line",
            data: {
                labels: labels,
                datasets: [
                    {
                        label: "This period",
                        data: days.map(function (d) { return isNet ? d.net : d.orders; }),
                        borderColor: "#C9392B",
                        backgroundColor: fill,
                        fill: true,
                        tension: .35,
                        borderWidth: 2.5,
                        pointRadius: days.length > 40 ? 0 : 3.5,
                        pointHoverRadius: 6,
                        pointBackgroundColor: "#C9392B"
                    },
                    {
                        label: "Previous",
                        data: days.map(function (d) { return isNet ? d.prevNet : d.prevOrders; }),
                        borderColor: "#B9A491",
                        borderDash: [5, 4],
                        borderWidth: 2,
                        pointRadius: 0,
                        tension: .35
                    }
                ]
            },
            options: {
                maintainAspectRatio: false,
                interaction: { mode: "index", intersect: false },
                plugins: {
                    legend: { display: false },
                    tooltip: Object.assign({}, tooltip, {
                        callbacks: {
                            title: function (items) { return days[items[0].dataIndex].title; },
                            label: function (c) {
                                var d = days[c.dataIndex];
                                return " " + (c.datasetIndex === 0 ? "This period" : "Previous (" + d.prevLabel + ")") + ": " + fmt(c.raw);
                            },
                            footer: function () { return "Click to open this day's report"; }
                        }
                    })
                },
                scales: {
                    x: { grid: { display: false }, ticks: { maxTicksLimit: 10, maxRotation: 0 } },
                    y: {
                        beginAtZero: true,
                        grid: grid,
                        border: { display: false },
                        ticks: {
                            precision: 0,
                            callback: function (v) { return isNet && v >= 1000 ? (v / 1000) + "k" : v; }
                        }
                    }
                },
                onClick: function (e, items) {
                    if (items.length) { goTo(days[items[0].index].salesLink); }
                },
                onHover: function (e, items) {
                    e.native.target.style.cursor = items.length ? "pointer" : "default";
                }
            }
        });

        var sub = page.querySelector("[data-db-sales-sub]");
        if (sub) { sub.textContent = isNet ? "Net sales per day (RM)" : "Orders per day"; }
    }

    page.addEventListener("click", function (event) {
        var b = event.target.closest("[data-db-mode]");
        if (!b) { return; }
        mode = b.getAttribute("data-db-mode");
        page.querySelectorAll("[data-db-mode]").forEach(function (x) {
            var on = x === b;
            x.classList.toggle("is-on", on);
            x.setAttribute("aria-pressed", on ? "true" : "false");
        });
        drawSales();
    });

    drawSales();

    // Doughnut: top 5 dishes.
    var dishCanvas = document.getElementById("dbDishes");
    if (dishCanvas && data.dishes.length) {
        var total = data.dishes.reduce(function (a, d) { return a + d.portions; }, 0);
        new Chart(dishCanvas, {
            type: "doughnut",
            data: {
                labels: data.dishes.map(function (d) { return d.name; }),
                datasets: [{
                    data: data.dishes.map(function (d) { return d.portions; }),
                    backgroundColor: ["#C9392B", "#1E1A18", "#C9A26A", "#3D5A80", "#3F7A4C"],
                    borderColor: "#FFFCF7",
                    borderWidth: 3,
                    hoverOffset: 8
                }]
            },
            options: {
                cutout: "68%",
                maintainAspectRatio: false,
                plugins: {
                    legend: { display: false },
                    tooltip: Object.assign({}, tooltip, {
                        callbacks: {
                            label: function (c) { return " " + c.raw + " portions (" + Math.round(c.raw * 100 / total) + "%)"; }
                        }
                    })
                }
            }
        });
    }

    // Stacked bar: reservations per day by status.
    var resCanvas = document.getElementById("dbReservations");
    if (resCanvas) {
        new Chart(resCanvas, {
            type: "bar",
            data: {
                labels: labels,
                datasets: data.statuses.map(function (s) {
                    return {
                        label: s.name,
                        data: days.map(function (d) { return d.res[s.name] || 0; }),
                        backgroundColor: s.color,
                        borderRadius: 4,
                        maxBarThickness: 28
                    };
                })
            },
            options: {
                maintainAspectRatio: false,
                interaction: { mode: "index", intersect: false },
                plugins: {
                    legend: {
                        position: "bottom",
                        labels: { boxWidth: 10, boxHeight: 10, usePointStyle: true, pointStyle: "rectRounded", padding: 14 }
                    },
                    tooltip: Object.assign({}, tooltip, {
                        filter: function (item) { return item.raw > 0; },
                        callbacks: {
                            title: function (items) { return days[items[0].dataIndex].title; },
                            footer: function (items) {
                                var n = items.reduce(function (a, x) { return a + x.raw; }, 0);
                                return "Total: " + n + (n === 1 ? " reservation" : " reservations");
                            }
                        }
                    })
                },
                scales: {
                    x: { stacked: true, grid: { display: false }, ticks: { maxTicksLimit: 10, maxRotation: 0 } },
                    y: { stacked: true, beginAtZero: true, ticks: { precision: 0 }, grid: grid, border: { display: false } }
                },
                onClick: function (e, items) {
                    if (items.length) { goTo(days[items[0].index].resLink); }
                },
                onHover: function (e, items) {
                    e.native.target.style.cursor = items.length ? "pointer" : "default";
                }
            }
        });
    }
})();