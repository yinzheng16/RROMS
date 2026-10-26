// =========================================================
// US43 Admin feedback (Member 5)
// Pages/Admin/Feedback/Index
// - Reply boxes: character counter, quick phrases, no empty replies
// - Filters (rating stamps, bars, chips, sort, search, side index) load
//   in the background, so the page does not reload or jump to the top.
//   The URL still updates (?rating=5…), so Back / Refresh / sharing work.
//   Without JavaScript the filters are normal links and still work.
// =========================================================
(function () {
    "use strict";

    var page = document.querySelector(".af-page");
    if (!page) {
        return;
    }

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

    // ---------- Side index highlight ----------
    function setSideActive(link) {
        page.querySelectorAll(".af-side-link").forEach(function (a) {
            a.classList.toggle("is-on", a === link);
        });
    }

    // ---------- Background filtering ----------
    // Parts of the page that change when a filter changes.
    var PARTS = [".af-dist", "#af-list .af-sec-body", ".af-side"];

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

    page.addEventListener("click", function (event) {
        var link = event.target.closest("a");
        if (!link || !page.contains(link)) {
            return;
        }

        // Let Cmd/Ctrl/Shift-click open a new tab as usual.
        if (event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) {
            return;
        }

        if (link.classList.contains("af-side-link")) {
            setSideActive(link);
        }

        var href = link.getAttribute("href") || "";

        // "一 Summary": smooth scroll, no "#" left in the address bar.
        if (href.charAt(0) === "#") {
            event.preventDefault();
            var section = document.querySelector(href);
            if (section) { section.scrollIntoView({ behavior: "smooth", block: "start" }); }
            return;
        }

        if (isFilterLink(link)) {
            event.preventDefault();

            // The side links also take you to the list.
            if (link.classList.contains("af-side-link")) {
                var listSection = document.getElementById("af-list");
                if (listSection) { listSection.scrollIntoView({ behavior: "smooth", block: "start" }); }
            }

            load(link.href, true);
        }
    });

    // Search box: filter in the background too.
    page.addEventListener("submit", function (event) {
        var form = event.target;
        if (!form.classList.contains("af-search")) {
            return;
        }

        event.preventDefault();

        var params = new URLSearchParams(new FormData(form));
        Array.from(params.keys()).forEach(function (key) {
            if (!params.get(key)) { params.delete(key); }
        });

        var query = params.toString();
        load("/Admin/Feedback" + (query ? "?" + query : ""), true);
    });

    // Browser Back / Forward between filters.
    window.addEventListener("popstate", function () {
        load(window.location.href, false);
    });

    // Opened with #af-summary in the address: highlight Summary.
    if (window.location.hash === "#af-summary") {
        setSideActive(page.querySelector('.af-side-link[href="#af-summary"]'));
    }

    bindReplyForms(page);
})();
