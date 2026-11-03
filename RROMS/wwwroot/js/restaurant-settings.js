// =========================================================
// US49 Restaurant Settings (Member 5; redesign by Member 1)
// Live business card, rate controls, example receipt, phone
// check and the "unsaved changes" bar for Pages/Admin/Settings.cshtml.
// =========================================================
(function () {
    "use strict";

    var page = document.getElementById("rsPage");
    var form = document.getElementById("rsForm");
    if (!page || !form) { return; }

    function $(id) { return document.getElementById(id); }

    var country = $("RestaurantSetting_PhoneCountry");
    var phone = $("PhoneInput");
    var phoneBox = $("phoneBox");
    var phoneMsg = $("phoneMsg");
    var phoneServerMsg = $("phoneServerMsg");
    var email = $("RestaurantSetting_Email");
    var address = $("RestaurantSetting_Address");
    var svc = $("RestaurantSetting_ServiceChargePercent");
    var tax = $("RestaurantSetting_TaxRatePercent");
    var take = $("RestaurantSetting_ServiceChargeOnTakeaway");
    var biz = $("RestaurantSetting_BusinessName");   // US49 follow-up
    var sst = $("RestaurantSetting_SstRegistrationNo");
    var note = $("RestaurantSetting_ReceiptFooterNote");

    // The fields that are saved. The save bar counts how many differ from when the page opened.
    var tracked = [country, phone, email, address, svc, tax, take, biz, sst, note];
    function valueOf(el) { return el.type === "checkbox" ? el.checked : el.value; }
    var initial = tracked.map(valueOf);

    // ---------- helpers ----------
    function rate(input) {
        var v = input.value.trim();
        if (v === "") { return null; }
        var n = Number(v);
        return isNaN(n) || n < 0 || n > 100 ? null : n;
    }
    function round2(n) { return Math.round((n + Number.EPSILON) * 100) / 100; }
    function pct(n) { return n === null ? "—" : String(round2(n)) + "%"; }
    function money(n) { return n === null ? "—" : n.toFixed(2); }
    function setText(id, text) { var el = $(id); if (el) { el.textContent = text; } }

    function selectedCountry() {
        var o = country.options[country.selectedIndex];
        return { iso: country.value, name: o ? o.dataset.name : country.value, dial: o ? o.dataset.dial : "" };
    }

    // "03-2142 6688" + MY -> "+60 3-2142 6688" (same as PhoneDisplay.Pretty on the server)
    function prettyPhone() {
        var v = phone.value.trim();
        if (!v) { return "—"; }
        var c = selectedCountry();
        try {
            if (window.libphonenumber) {
                var p = window.libphonenumber.parsePhoneNumber(v, c.iso);
                if (p) { return p.formatInternational(); }
            }
        } catch (e) { /* show what was typed */ }
        return c.dial + " " + v.replace(/^0/, "");
    }

    // ---------- phone check (same rules as the server) ----------
    function checkPhone() {
        var c = selectedCountry();
        var v = phone.value.trim();
        var msg = "";
        if (v) {
            if (!/^[0-9+\-\s()]+$/.test(v)) {
                msg = "Phone number can only contain digits.";
            } else if (window.libphonenumber && !window.libphonenumber.isValidPhoneNumber(v, c.iso)) {
                msg = "This is not a valid " + c.name + " (" + c.dial + ") phone number.";
            }
        }
        phoneMsg.textContent = msg;
        phoneMsg.hidden = !msg;
        phoneBox.classList.toggle("is-err", !!msg);
        return !msg;
    }

    // ---------- counters ----------
    function updateCounters() {
        page.querySelectorAll("[data-count-for]").forEach(function (c) {
            var input = $(c.dataset.countFor);
            if (!input) { return; }
            var max = parseInt(c.dataset.max, 10), len = input.value.length;
            c.textContent = len + " / " + max;
            c.classList.toggle("is-over", len > max);
        });
    }

    // ---------- rate tiles: −/+, slider, quick picks ----------
    page.querySelectorAll("[data-rate]").forEach(function (tile) {
        var input = tile.querySelector(".rs-big");
        var range = tile.querySelector(".rs-range");
        var chips = tile.querySelectorAll(".rs-chip");

        function setValue(n) {
            input.value = String(round2(Math.min(100, Math.max(0, n))));
            input.dispatchEvent(new Event("input", { bubbles: true }));
        }

        tile.querySelectorAll(".rs-step").forEach(function (b) {
            b.addEventListener("click", function () { setValue((rate(input) || 0) + Number(b.dataset.step)); });
        });
        chips.forEach(function (c) {
            c.addEventListener("click", function () { setValue(Number(c.dataset.set)); });
        });
        range.addEventListener("input", function () { setValue(Number(range.value)); });

        tile.paint = function () {
            var n = rate(input);
            // size the box to the number so "10%" sits in the middle of the tile
            input.style.width = Math.max(1, input.value.length) * 0.58 + "em";
            var shown = n === null ? 0 : Math.min(30, n);   // slider shows 0–30%; the box allows up to 100
            range.value = shown;
            range.style.setProperty("--p", (shown / 30 * 100) + "%");
            chips.forEach(function (c) { c.classList.toggle("is-on", n !== null && Number(c.dataset.set) === n); });
        };
    });

    // ---------- live previews ----------
    // Same maths as ReportService.CalculateCharges (keep in sync):
    //   service = subtotal x service%  (0 for takeaway unless the switch is on)
    //   tax     = (subtotal + service) x tax%
    function render() {
        var s = rate(svc), t = rate(tax);
        var isTake = (form.querySelector('input[name="pvType"]:checked') || {}).value === "take";
        var applies = !isTake || take.checked;
        var service = s === null ? null : (applies ? round2(100 * s / 100) : 0);
        var taxAmt = t === null || service === null ? null : round2((100 + service) * t / 100);
        var total = taxAmt === null ? null : 100 + service + taxAmt;

        // hero pills
        setText("glSvc", pct(s));
        setText("glTake", take.checked ? "dine-in & takeaway" : "dine-in only");
        setText("glTax", pct(t));
        setText("glPhone", prettyPhone());

        // business card
        setText("mcAddr", address.value.trim() || "—");
        setText("mcPhone", prettyPhone());
        setText("mcEmail", email.value.trim());
        $("mcEmailRow").hidden = !email.value.trim();

        // takeaway switch text
        setText("takeHelp", take.checked ? "On: service charge added" : "Off: no service charge");

        // equation strip
        setText("eqTitle", "HOW A RM 100 " + (isTake ? "TAKEAWAY" : "DINE-IN") + " BILL ADDS UP");
        setText("eqSvcL", applies ? "SERVICE · " + pct(s) : "SERVICE · not on takeaway");
        setText("eqSvc", service === null ? "—" : "RM " + money(service));
        setText("eqTaxL", "SST · " + pct(t) + " incl. service");
        setText("eqTax", taxAmt === null ? "—" : "RM " + money(taxAmt));
        setText("eqTotal", total === null ? "Check rates" : "RM " + money(total));

        // receipt
        var bizText = biz.value.trim();
        setText("rcBiz", bizText);
        $("rcBiz").hidden = !bizText;
        var sstText = sst.value.trim().toUpperCase();
        setText("rcSst", "SST No. " + sstText);
        $("rcSst").hidden = !sstText;
        setText("rcAddr", address.value.trim().replace(/,?\s*\n\s*/g, ", "));
        setText("rcKind", isTake ? "Takeaway · #T-012" : "Table 7 · Dine-in");
        setText("rcSvcL", applies ? "Service charge (" + pct(s) + ")" : "Service charge (not on takeaway)");
        $("rcSvcRow").classList.toggle("is-off", !applies);
        setText("rcSvc", money(service));
        setText("rcTaxL", "SST (" + pct(t) + ")");
        setText("rcTax", money(taxAmt));
        setText("rcTotal", total === null ? "—" : "RM " + money(total));
        setText("rcNote", note.value.trim());
        $("rcNote").hidden = !note.value.trim();

        // SST check (same text as Settings.SstWarning on the server)
        var warn = "";
        if (t !== null && t > 0 && !sstText) {
            warn = "SST is " + pct(t) + " but there is no SST registration number. Add it, or set the tax rate to 0%.";
        } else if (sstText && !bizText) {
            warn = "Add the registered business name too. Receipts with an SST number should show it.";
        }
        setText("sstWarnText", warn);
        $("sstWarn").hidden = !warn;

        page.querySelectorAll("[data-rate]").forEach(function (tile) { tile.paint(); });
        updateCounters();
        updateSaveBar();
    }

    // ---------- save bar: only shown when something changed ----------
    function changedCount() {
        return tracked.filter(function (el, i) { return valueOf(el) !== initial[i]; }).length;
    }

    function updateSaveBar() {
        if (page.classList.contains("has-errors")) { return; }   // after a failed save it stays up
        var n = changedCount();
        page.classList.toggle("is-dirty", n > 0);
        if (n > 0) { setText("rsSaveCount", n + (n === 1 ? " unsaved change" : " unsaved changes")); }
    }

    $("rsDiscard").addEventListener("click", function () {
        tracked.forEach(function (el, i) {
            if (el.type === "checkbox") { el.checked = initial[i]; } else { el.value = initial[i]; }
        });
        page.classList.remove("has-errors");
        country.dispatchEvent(new Event("change", { bubbles: true }));   // repaints the flag button
        checkPhone();
        render();
    });

    // Leaving with unsaved changes: the browser asks first.
    var submitting = false;
    window.addEventListener("beforeunload", function (e) {
        if (!submitting && changedCount() > 0) { e.preventDefault(); e.returnValue = ""; }
    });

    // ---------- events ----------
    // Any typing or change redraws the previews and recounts unsaved changes.
    // (The country search box, the sliders and the Dine-in/Takeaway switch are not saved fields,
    // so they never count as a change: only the fields in "tracked" do.)
    form.addEventListener("input", render);
    form.addEventListener("change", render);

    phone.addEventListener("input", function () { if (phoneServerMsg) { phoneServerMsg.hidden = true; } checkPhone(); });
    country.addEventListener("change", function () { if (phoneServerMsg) { phoneServerMsg.hidden = true; } checkPhone(); });

    form.addEventListener("submit", function (e) {
        var jqOk = !(window.jQuery && window.jQuery(form).valid && !window.jQuery(form).valid());
        var phoneOk = checkPhone();
        if (!jqOk || !phoneOk) {
            e.preventDefault();
            page.classList.add("has-errors");
            setText("rsSaveCount", "Not saved yet");
            var first = page.querySelector(".input-validation-error, .rs-phone-row.is-err");
            if (first) { first.scrollIntoView({ behavior: "smooth", block: "center" }); }
            return;
        }
        submitting = true;
    });

    // ---------- country picker (same as /Account/Register, Member 1) ----------
    page.querySelectorAll("[data-cc]").forEach(function (box) {
        var select = box.querySelector(".cc-native");
        var btn = box.querySelector(".cc-btn");
        var panel = box.querySelector(".cc-panel");
        var search = box.querySelector(".cc-input");
        var list = box.querySelector(".cc-list");
        var empty = box.querySelector(".cc-empty");
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
            phone.focus();
        }

        select.addEventListener("change", paint);   // also repaints after Discard
        btn.addEventListener("click", function () { panel.hidden ? open() : close(true); });
        search.addEventListener("input", filter);
        search.addEventListener("keydown", function (e) {
            if (e.key === "ArrowDown") { e.preventDefault(); highlight(active + 1); }
            else if (e.key === "ArrowUp") { e.preventDefault(); highlight(active - 1); }
            else if (e.key === "Enter") { e.preventDefault(); var r = visible(); if (r[active]) { choose(r[active].dataset.value); } }
            else if (e.key === "Escape") { close(true); }
        });
        document.addEventListener("click", function (e) { if (!box.contains(e.target)) { close(false); } });
        paint();
    });

    // ---------- first paint ----------
    if (!phoneServerMsg || !phoneServerMsg.textContent.trim()) { checkPhone(); }
    render();
})();
