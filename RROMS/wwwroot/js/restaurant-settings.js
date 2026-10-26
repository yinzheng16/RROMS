// =========================================================
// US49 Restaurant Settings (Member 5)
// Live previews, counters, phone check and toast for
// Pages/Admin/Settings.cshtml. No other page uses this file.
// =========================================================
(function () {
    "use strict";

    var page = document.getElementById("rsPage");
    var form = document.getElementById("rsForm");
    if (!page || !form) {
        return;
    }

    function byId(id) {
        return document.getElementById(id);
    }

    // ---------- Elements ----------
    var nameInput = byId("RestaurantSetting_RestaurantName");
    var line2Input = byId("RestaurantSetting_RestaurantNameLine2");
    var logoFile = byId("LogoFile");
    var footerFile = byId("FooterFile");
    var faviconFile = byId("FaviconFile");
    var footerSame = byId("FooterSameAsNav");
    var removeFavicon = byId("RemoveFavicon");
    var country = byId("RestaurantSetting_PhoneCountry");
    var phone = byId("PhoneInput");
    var phoneBox = byId("phoneBox");
    var phoneMsg = byId("phoneMsg");
    var phoneServerMsg = byId("phoneServerMsg");
    var email = byId("RestaurantSetting_Email");
    var address = byId("RestaurantSetting_Address");
    var tax = byId("RestaurantSetting_TaxRatePercent");
    var service = byId("RestaurantSetting_ServiceChargePercent");
    var saveState = byId("rsSaveState");
    var toast = byId("rsToast");

    // Navbar text sizes for S / M / L (px). Keep in sync with the shared layout.
    var SIZES = {
        line1: { S: 18, M: 22, L: 27 },
        line2: { S: 9, M: 11, L: 13 }
    };

    // Saved image paths (from the database) and newly picked files (object URLs).
    var saved = {
        nav: page.dataset.logo || "",
        footer: page.dataset.footer || "",
        favicon: page.dataset.favicon || "",
        defaultFavicon: page.dataset.defaultFavicon || ""
    };
    var picked = { nav: "", footer: "", favicon: "" };
    var faviconRemoved = false;

    // ---------- Small helpers ----------
    function radioValue(name) {
        var checked = form.querySelector('input[name="' + name + '"]:checked');
        return checked ? checked.value : "";
    }

    function isFullBrandImage() {
        return radioValue("RestaurantSetting.BrandingMode") === "FullBrandImage";
    }

    function showImage(img, placeholder, src) {
        if (src) {
            img.src = src;
            img.hidden = false;
            if (placeholder) { placeholder.hidden = true; }
        } else {
            img.removeAttribute("src");
            img.hidden = true;
            if (placeholder) { placeholder.hidden = false; }
        }
    }

    function makeImg(src, className) {
        var img = document.createElement("img");
        img.src = src;
        img.alt = "";
        img.className = className;
        return img;
    }

    function makePlaceholder() {
        var span = document.createElement("span");
        span.className = "rs-ph";
        span.setAttribute("aria-hidden", "true");
        span.textContent = "夜";
        return span;
    }

    function selectedCountry() {
        var option = country.options[country.selectedIndex];
        return {
            iso: country.value,
            name: option ? option.dataset.name : country.value,
            dial: option ? option.dataset.dial : ""
        };
    }

    // ---------- Character counters ----------
    function updateCounters() {
        page.querySelectorAll("[data-count-for]").forEach(function (counter) {
            var input = byId(counter.dataset.countFor);
            if (!input) { return; }
            var max = parseInt(counter.dataset.max, 10);
            var length = input.value.length;
            counter.textContent = length + " / " + max;
            counter.classList.toggle("is-over", length > max);
        });
    }

    // ---------- Phone check (same rules as the server) ----------
    function checkPhone() {
        var c = selectedCountry();
        var value = phone.value.trim();
        var message = "";

        if (value) {
            if (!/^[0-9+\-\s()]+$/.test(value)) {
                message = "Phone number can only contain digits.";
            } else if (window.libphonenumber &&
                       !window.libphonenumber.isValidPhoneNumber(value, c.iso)) {
                message = "This is not a valid " + c.name + " (" + c.dial + ") phone number.";
            }
        }

        phoneMsg.textContent = message;
        phoneMsg.hidden = !message;
        phoneBox.classList.toggle("is-err", !!message);
        return !message;
    }

    // ---------- Live previews ----------
    function render() {
        var full = isFullBrandImage();
        page.classList.toggle("rs-mode-full", full);

        var line1 = nameInput.value.trim();
        var line2 = line2Input ? line2Input.value.trim() : "";

        // Images
        var navSrc = picked.nav || saved.nav;
        showImage(byId("imgNav"), byId("phNav"), navSrc);

        var footerSrc = footerSame.checked ? navSrc : (picked.footer || saved.footer || navSrc);
        showImage(byId("imgFooter"), byId("phFooter"), footerSrc);
        byId("slotFooter").classList.toggle("is-disabled", footerSame.checked);

        var faviconSrc = picked.favicon || (faviconRemoved ? "" : saved.favicon) || saved.defaultFavicon;
        showImage(byId("imgFavicon"), null, faviconSrc);

        // Navbar preview
        var brand = byId("pvBrand");
        brand.textContent = "";

        if (full) {
            brand.appendChild(navSrc ? makeImg(navSrc, "rs-pv-full") : makePlaceholder());
        } else {
            brand.appendChild(navSrc ? makeImg(navSrc, "rs-pv-logo") : makePlaceholder());

            var names = document.createElement("div");
            names.className = "rs-pv-name";

            var l1 = document.createElement("span");
            l1.className = "rs-pv-l1";
            l1.style.fontSize = SIZES.line1[radioValue("RestaurantSetting.NameLine1Size") || "M"] + "px";
            l1.textContent = line1 || "Restaurant name";
            names.appendChild(l1);

            if (line2) {
                var l2 = document.createElement("span");
                l2.className = "rs-pv-l2";
                l2.style.fontSize = SIZES.line2[radioValue("RestaurantSetting.NameLine2Size") || "S"] + "px";
                l2.textContent = line2;
                names.appendChild(l2);
            }

            brand.appendChild(names);
        }

        // Footer preview
        var footImg = byId("pvFootImg");
        if (footerSrc) {
            footImg.src = footerSrc;
            footImg.hidden = false;
        } else {
            footImg.hidden = true;
        }

        var footName = byId("pvFootName");
        footName.textContent = [line1, line2].filter(Boolean).join(" · ");
        footName.hidden = full;

        byId("pvFootAddr").textContent = address.value.trim();

        var c = selectedCountry();
        var contact = [];
        if (phone.value.trim()) { contact.push(c.dial + " " + phone.value.trim()); }
        if (email.value.trim()) { contact.push(email.value.trim()); }
        byId("pvFootContact").textContent = contact.join("  ·  ");

        // Browser tab preview
        var tabIcon = byId("pvFavicon");
        if (faviconSrc) {
            tabIcon.src = faviconSrc;
            tabIcon.hidden = false;
        } else {
            tabIcon.hidden = true;
        }
        byId("pvTabTitle").textContent = "Restaurant Settings - " + (line1 || "RROMS");

        renderReceipt();
        updateCounters();
    }

    // ---------- Example bill ----------
    function readRate(input) {
        var value = input.value.trim();
        if (value === "") { return null; }
        var number = Number(value);
        return isNaN(number) || number < 0 || number > 100 ? null : number;
    }

    function money(value) {
        return "RM " + value.toFixed(2);
    }

    function renderReceipt() {
        var subtotal = 100;
        var serviceRate = readRate(service);
        var taxRate = readRate(tax);

        byId("rcSvcLabel").textContent = "Service charge (" + (serviceRate === null ? "—" : serviceRate) + "%)";
        byId("rcTaxLabel").textContent = "Tax (" + (taxRate === null ? "—" : taxRate) + "%)";

        var serviceAmount = serviceRate === null ? null : subtotal * serviceRate / 100;
        var taxAmount = taxRate === null ? null : subtotal * taxRate / 100;

        byId("rcSvc").textContent = serviceAmount === null ? "—" : money(serviceAmount);
        byId("rcTax").textContent = taxAmount === null ? "—" : money(taxAmount);
        byId("rcTotal").textContent = serviceAmount === null || taxAmount === null
            ? "Fix the rates above"
            : money(subtotal + serviceAmount + taxAmount);
    }

    // ---------- Unsaved-changes indicator ----------
    function markDirty() {
        saveState.textContent = "● Unsaved changes";
        saveState.classList.add("is-dirty");
    }

    // ---------- Toast ----------
    var toastTimer;

    function showToast(autohide) {
        if (!toast) { return; }
        toast.classList.add("is-show");
        clearTimeout(toastTimer);
        if (autohide) {
            toastTimer = setTimeout(function () { toast.classList.remove("is-show"); }, 3600);
        }
    }

    if (toast) {
        toast.querySelector(".rs-toast-close").addEventListener("click", function () {
            toast.classList.remove("is-show");
        });
        if (toast.classList.contains("is-show") && toast.dataset.autohide === "true") {
            showToast(true);
        }
    }

    // ---------- File inputs ----------
    function onFilePicked(input, key) {
        input.addEventListener("change", function () {
            var file = input.files && input.files[0];
            if (picked[key]) { URL.revokeObjectURL(picked[key]); }
            picked[key] = file ? URL.createObjectURL(file) : "";
            if (key === "favicon" && file) {
                faviconRemoved = false;
                removeFavicon.value = "false";
            }
            markDirty();
            render();
        });
    }

    onFilePicked(logoFile, "nav");
    onFilePicked(footerFile, "footer");
    onFilePicked(faviconFile, "favicon");

    // "Undo" clears a newly picked file; "Use default" removes the saved tab icon.
    page.querySelectorAll("[data-reset]").forEach(function (button) {
        button.addEventListener("click", function () {
            var key = button.dataset.reset;
            var input = { nav: logoFile, footer: footerFile, favicon: faviconFile }[key];

            if (picked[key]) { URL.revokeObjectURL(picked[key]); }
            picked[key] = "";
            input.value = "";

            if (key === "favicon") {
                faviconRemoved = true;
                removeFavicon.value = "true";
            }

            markDirty();
            render();
        });
    });

    footerSame.addEventListener("change", function () {
        if (footerSame.checked) {
            if (picked.footer) { URL.revokeObjectURL(picked.footer); }
            picked.footer = "";
            footerFile.value = "";
        }
    });

    // ---------- Events ----------
    // Typing in the country search box is not a settings change.
    function isPickerSearch(event) {
        return event.target.classList && event.target.classList.contains("cc-input");
    }

    form.addEventListener("input", function (event) {
        if (isPickerSearch(event)) { return; }
        markDirty();
        render();
    });

    form.addEventListener("change", function (event) {
        if (isPickerSearch(event)) { return; }
        markDirty();
        render();
    });

    phone.addEventListener("input", function () {
        if (phoneServerMsg) { phoneServerMsg.hidden = true; }
        checkPhone();
    });

    country.addEventListener("change", function () {
        if (phoneServerMsg) { phoneServerMsg.hidden = true; }
        checkPhone();
    });

    form.addEventListener("submit", function (event) {
        var jqueryValid = !(window.jQuery && window.jQuery(form).valid && !window.jQuery(form).valid());
        var phoneValid = checkPhone();

        if (!phoneValid || !jqueryValid) {
            event.preventDefault();
            showToast(true);
            var firstError = page.querySelector(".input-validation-error, .rs-phone-row.is-err");
            if (firstError) {
                firstError.scrollIntoView({ behavior: "smooth", block: "center" });
            }
        }
    });

    // ---------- Country picker (same as /Account/Register, Member 1) ----------
    // Copied from Register.cshtml so Member 1's page is not touched.
    // Only change: after choosing, focus goes to PhoneInput instead of Input_PhoneNumber.
    page.querySelectorAll("[data-cc]").forEach(function (box) {
        var select = box.querySelector(".cc-native");
        var btn = box.querySelector(".cc-btn");
        var panel = box.querySelector(".cc-panel");
        var search = box.querySelector(".cc-input");
        var list = box.querySelector(".cc-list");
        var empty = box.querySelector(".cc-empty");
        var active = -1;   // highlighted row while using the arrow keys

        box.classList.add("cc-ready");   // hides the plain select, shows the button

        // One row per country: [MY]  Malaysia ............ +60
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

        function close(backToButton) {
            panel.hidden = true;
            btn.setAttribute("aria-expanded", "false");
            box.classList.remove("is-open");
            if (backToButton) { btn.focus(); }
        }

        function choose(value) {
            select.value = value;
            select.dispatchEvent(new Event("change", { bubbles: true }));
            paint();
            close(false);
            phone.focus();   // ready to type the number
        }

        btn.addEventListener("click", function () { panel.hidden ? open() : close(true); });
        search.addEventListener("input", filter);
        search.addEventListener("keydown", function (e) {
            if (e.key === "ArrowDown") { e.preventDefault(); highlight(active + 1); }
            else if (e.key === "ArrowUp") { e.preventDefault(); highlight(active - 1); }
            else if (e.key === "Enter") {
                e.preventDefault();   // don't submit the form
                var rows = visible();
                if (rows[active]) { choose(rows[active].dataset.value); }
            }
            else if (e.key === "Escape") { close(true); }
        });
        document.addEventListener("click", function (e) { if (!box.contains(e.target)) { close(false); } });

        paint();
    });

    // ---------- Side index highlight ----------
    var sections = Array.prototype.slice.call(page.querySelectorAll(".rs-sec"));
    var links = Array.prototype.slice.call(page.querySelectorAll(".rs-side a"));

    function setActiveLink(id) {
        links.forEach(function (link) {
            link.classList.toggle("is-on", link.getAttribute("href") === "#" + id);
        });
    }

    // Section chosen by clicking a link. It stays red until the admin
    // scrolls again by hand AFTER the smooth scroll has finished.
    var clickedId = "";
    var autoScrolling = false;   // true while the smooth scroll from a click is still moving
    var settleTimer;

    // Scroll with JavaScript instead of changing the URL to "#rs-charges".
    // Otherwise the hash stays in the address bar, and after Save the page
    // reloads and jumps straight back down to that section.
    links.forEach(function (link) {
        link.addEventListener("click", function (event) {
            event.preventDefault();
            clickedId = link.getAttribute("href").slice(1);
            autoScrolling = true;
            setActiveLink(clickedId);
            var target = byId(clickedId);
            if (target) { target.scrollIntoView({ behavior: "smooth", block: "start" }); }
            // If the page is already at that spot there is no scroll event, so settle anyway.
            clearTimeout(settleTimer);
            settleTimer = setTimeout(function () { autoScrolling = false; }, 200);
        });
    });

    // Clean up an old "#rs-..." left in the address bar (e.g. from a bookmark or an earlier version).
    if (/^#rs-/.test(window.location.hash)) {
        history.replaceState(null, "", window.location.pathname + window.location.search);
        window.addEventListener("load", function () { window.scrollTo(0, 0); });
    }

    // A real scroll by the admin (not trackpad momentum during the smooth scroll)
    // hands control back to the normal "which section is on screen" rule.
    function userScrolled() {
        if (!autoScrolling) { clickedId = ""; }
    }

    window.addEventListener("wheel", userScrolled, { passive: true });
    window.addEventListener("touchmove", userScrolled, { passive: true });
    window.addEventListener("keydown", userScrolled);

    function sectionOnScreen() {
        var current = sections[0] ? sections[0].id : "";
        sections.forEach(function (section) {
            if (section.getBoundingClientRect().top < 160) { current = section.id; }
        });

        // The last section is short and can never reach the top of the screen,
        // so treat "scrolled to the bottom" as being on the last section.
        var scrolled = Math.ceil(window.innerHeight + window.scrollY);
        var atBottom = scrolled >= document.documentElement.scrollHeight - 8;
        if (atBottom && sections.length) { current = sections[sections.length - 1].id; }

        return current;
    }

    window.addEventListener("scroll", function () {
        if (clickedId) {
            // Still finishing the smooth scroll: wait until it stops moving.
            clearTimeout(settleTimer);
            settleTimer = setTimeout(function () { autoScrolling = false; }, 150);
            return;
        }
        setActiveLink(sectionOnScreen());
    }, { passive: true });

    // ---------- First paint ----------
    // Show a server-side phone error as-is; only re-check after the admin edits.
    if (!phoneServerMsg || !phoneServerMsg.textContent.trim()) {
        checkPhone();
    }
    render();
})();
