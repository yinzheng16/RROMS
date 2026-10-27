// =========================================================
// US43 Admin feedback (Member 5)
// Pages/Admin/Feedback/Index
// - Reply boxes: character counter, quick phrases, no empty replies
// - Sort menu with hanko stamps (新 newest / 低 lowest)
// - Filters (rating stamps, bars, chips, sort, search) load in the
//   background, so the page does not reload or jump to the top.
//   The URL still updates (?rating=5…), so Back / Refresh / sharing work.
//   Without JavaScript the filters are normal links, the sort is a plain
//   select, and everything still works.
// =========================================================
(function () {
    "use strict";

    var page = document.querySelector(".af-page");
    if (!page) {
        return;
    }

    // Tells the CSS to show the hanko sort menu instead of the plain select.
    page.classList.add("af-js");

    var MAX = 500;
    var loading = false;

    // ---------- Reply boxes ----------
    function bindReplyForms(root) {
        root.querySelectorAll(".af-reply form").forEach(function (form) {
            if (form.dataset.bound) {
                return;
            }
            form.dataset.bound = "1";

            var text = form.querySelector(".af-reply-text");
            var count = form.querySelector(".af-count");
            if (!text || !count) {
                return;
            }

            function update() {
                count.textContent = text.value.length;
                count.classList.toggle("is-over", text.value.length > MAX);
            }

            text.addEventListener("input", update);

            form.querySelectorAll(".af-quick-btn").forEach(function (button) {
                button.addEventListener("click", function () {
                    var phrase = button.textContent.trim();
                    var spacer = text.value && !/\s$/.test(text.value) ? " " : "";

                    if (text.value.length + spacer.length + phrase.length <= MAX) {
                        text.value = text.value + spacer + phrase;
                        update();
                        text.focus();
                    }
                });
            });

            // Don't send an empty reply (the server checks this too).
            form.addEventListener("submit", function (event) {
                if (!text.value.trim()) {
                    event.preventDefault();
                    text.value = "";
                    text.reportValidity();
                }
            });

            update();
        });
    }

    // ---------- Sort menu ----------
    function openSort(sort, open) {
        if (!sort) {
            return;
        }
        sort.classList.toggle("is-open", open);
        var btn = sort.querySelector(".af-sort-btn");
        if (btn) {
            btn.setAttribute("aria-expanded", open ? "true" : "false");
        }
        if (open) {
            var current = sort.querySelector(".af-sort-opt.is-on") || sort.querySelector(".af-sort-opt");
            if (current) { current.focus(); }
        }
    }

    function closeAllSorts() {
        page.querySelectorAll(".af-sort.is-open").forEach(function (sort) {
            openSort(sort, false);
        });
    }

    function pickSort(option) {
        var sort = option.closest(".af-sort");
        var form = option.closest("form");
        var select = form && form.querySelector(".af-sort-select");

        sort.querySelectorAll(".af-sort-opt").forEach(function (o) {
            var on = o === option;
            o.classList.toggle("is-on", on);
            o.setAttribute("aria-selected", on ? "true" : "false");
        });
        sort.querySelector(".af-sort-label").textContent = option.dataset.label;
        openSort(sort, false);
        sort.querySelector(".af-sort-btn").focus();

        if (select && select.value !== option.dataset.value) {
            select.value = option.dataset.value;
            submitSearch(form);
        }
    }

    // ---------- Background filtering ----------
    // Parts of the page that change when a filter changes.
    var PARTS = [".af-dist", "#af-list .af-sec-body"];

    function isFilterLink(a) {
        var href = a.getAttribute("href") || "";
        return href.indexOf("/Admin/Feedback") === 0;
    }

    function load(url, pushHistory) {
        if (loading) {
            return;
        }
        loading = true;

        var target = new URL(url, window.location.href);
        target.hash = "";

        var list = page.querySelector("#af-list .af-sec-body");
        if (list) { list.style.opacity = ".5"; }

        fetch(target.toString(), { credentials: "same-origin" })
            .then(function (response) {
                if (!response.ok) { throw new Error("HTTP " + response.status); }
                return response.text();
            })
            .then(function (html) {
                var doc = new DOMParser().parseFromString(html, "text/html");

                PARTS.forEach(function (selector) {
                    var fresh = doc.querySelector(selector);
                    var current = page.querySelector(selector);
                    if (fresh && current) {
                        current.innerHTML = fresh.innerHTML;
                    }
                });

                bindReplyForms(page);

                if (pushHistory) {
                    history.pushState({ af: true }, "", target.pathname + target.search);
                }
            })
            .catch(function () {
                // Something went wrong: fall back to a normal page load.
                window.location.href = target.toString();
            })
            .finally(function () {
                loading = false;
                var current = page.querySelector("#af-list .af-sec-body");
                if (current) { current.style.opacity = ""; }
            });
    }

    function submitSearch(form) {
        var params = new URLSearchParams(new FormData(form));
        Array.from(params.keys()).forEach(function (key) {
            if (!params.get(key)) { params.delete(key); }
        });

        var query = params.toString();
        load("/Admin/Feedback" + (query ? "?" + query : ""), true);
    }

    // ---------- Clicks ----------
    page.addEventListener("click", function (event) {
        // Sort menu button
        var sortBtn = event.target.closest(".af-sort-btn");
        if (sortBtn) {
            var sort = sortBtn.closest(".af-sort");
            openSort(sort, !sort.classList.contains("is-open"));
            return;
        }

        // Sort menu option
        var option = event.target.closest(".af-sort-opt");
        if (option) {
            pickSort(option);
            return;
        }

        var link = event.target.closest("a");
        if (!link || !page.contains(link)) {
            return;
        }

        // Let Cmd/Ctrl/Shift-click open a new tab as usual.
        if (event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) {
            return;
        }

        var href = link.getAttribute("href") || "";

        // In-page links: smooth scroll, no "#" left in the address bar.
        if (href.charAt(0) === "#") {
            event.preventDefault();
            var section = document.querySelector(href);
            if (section) { section.scrollIntoView({ behavior: "smooth", block: "start" }); }
            return;
        }

        if (isFilterLink(link)) {
            event.preventDefault();
            load(link.href, true);
        }
    });

    // Click anywhere else closes the sort menu.
    document.addEventListener("click", function (event) {
        if (!event.target.closest(".af-sort")) {
            closeAllSorts();
        }
    });

    // Keyboard in the sort menu: Esc closes, ↑ / ↓ move between options.
    page.addEventListener("keydown", function (event) {
        var sort = event.target.closest && event.target.closest(".af-sort");
        if (!sort) {
            return;
        }

        if (event.key === "Escape" && sort.classList.contains("is-open")) {
            event.preventDefault();
            openSort(sort, false);
            sort.querySelector(".af-sort-btn").focus();
            return;
        }

        if (event.key === "ArrowDown" || event.key === "ArrowUp") {
            event.preventDefault();
            if (!sort.classList.contains("is-open")) {
                openSort(sort, true);
                return;
            }
            var options = Array.from(sort.querySelectorAll(".af-sort-opt"));
            var index = options.indexOf(document.activeElement);
            var step = event.key === "ArrowDown" ? 1 : -1;
            var next = options[(index + step + options.length) % options.length];
            next.focus();
        }
    });

    // Search box: filter in the background too.
    page.addEventListener("submit", function (event) {
        var form = event.target;
        if (!form.classList.contains("af-search")) {
            return;
        }
        event.preventDefault();
        submitSearch(form);
    });

    // Browser Back / Forward between filters.
    window.addEventListener("popstate", function () {
        load(window.location.href, false);
    });

    bindReplyForms(page);
})();
