// Owner: Member 3. Category drafts stay outside the replaceable list workspace.
(() => {
    "use strict";
    if (typeof window.fetch !== "function" || typeof window.DOMParser !== "function" || typeof window.FormData !== "function") return;
    let panel = null, baseline = "", forcedDirty = false, busy = false, engaged = false;
    let pending = null, closeTimer = null, warning = false;
    const form = () => panel?.querySelector("[data-category-editor]");
    const editId = () => form()?.querySelector('[name="Input.Id"]')?.value || "";
    const name = () => form()?.querySelector('[name="Input.Name"]');
    const signature = () => JSON.stringify([editId(), name()?.value || ""]);
    const dirty = () => !!panel && (forcedDirty || baseline !== signature());
    const isOpen = () => !!panel?.classList.contains("is-open");
    const text = (node, value) => { if (node) node.textContent = value; };
    const announce = message => {
        let live = document.getElementById("category-drawer-live");
        if (!live) {
            live = document.createElement("span"); live.id = "category-drawer-live";
            live.className = "visually-hidden"; live.setAttribute("role", "status");
            live.setAttribute("aria-live", "polite"); document.body.append(live);
        }
        text(live, message);
    };
    const beforeLeave = event => { if (dirty()) { event.preventDefault(); event.returnValue = true; } };
    const sync = () => {
        const changed = dirty();
        text(panel?.querySelector("[data-editor-code]"), editId()
            ? panel.dataset.savedCode || "Unavailable — refresh the list"
            : "Assigned when saved");
        if (changed !== warning) {
            window[changed ? "addEventListener" : "removeEventListener"]("beforeunload", beforeLeave);
            warning = changed;
        }
        // Format only the preview. The single saved name retains both languages.
        const value = name()?.value.trim() || "Your category name";
        const separator = value.indexOf("·");
        const english = separator > 0 ? value.slice(separator + 1).trim() : "";
        text(panel?.querySelector("[data-category-preview-name]"), english ? value.slice(0, separator).trim() : value);
        const subtitle = panel?.querySelector("[data-category-preview-english]");
        text(subtitle, english);
        if (subtitle) subtitle.hidden = !english;
        text(panel?.querySelector("[data-category-name-count]"), String(name()?.value.length || 0));
        text(panel?.querySelector("[data-category-drawer-state]"), busy ? "Updating category…"
            : changed ? "Unsaved changes · kept while hidden" : editId() ? "Editing existing details" : "Ready for your next category");
        const active = isOpen() ? editId() : "";
        document.querySelectorAll("[data-category-row]").forEach(row => {
            const editing = !!active && row.dataset.categoryRow === active;
            row.dataset.editing = String(editing);
            row.classList.toggle("table-active", editing);
            const label = row.querySelector("[data-category-editing-label]");
            if (label) label.hidden = !editing;
            row.querySelector("[data-category-row-name]")?.classList.toggle("text-primary", editing);
            const order = row.querySelector("[data-category-order]");
            ["bg-primary", "text-white", "border-primary"].forEach(value => order?.classList.toggle(value, editing));
            const accent = row.querySelector("[data-category-row-accent]");
            accent?.classList.toggle("opacity-100", editing); accent?.classList.toggle("opacity-0", !editing);
        });
        document.querySelectorAll("[data-category-jump]").forEach(link => {
            const caption = link.querySelector("[data-menu-create-caption]");
            if (caption?.lastElementChild) {
                text(caption.lastElementChild, changed || editId() ? "Resume draft" : "New category");
                if (caption.firstElementChild !== caption.lastElementChild) caption.firstElementChild.hidden = !!(changed || editId());
            }
        });
        if (panel) panel.querySelectorAll("button").forEach(button => { button.disabled = busy; });
    };
    const finishHide = () => { if (!isOpen()) window.yoruMenuDrawerLayout?.deactivate(panel); };
    const hide = (focus = true) => {
        if (!panel) return;
        panel.classList.remove("is-open"); panel.inert = true; panel.setAttribute("aria-hidden", "true");
        window.clearTimeout(closeTimer);
        if (window.matchMedia?.("(prefers-reduced-motion: reduce)").matches) finishHide();
        else closeTimer = window.setTimeout(finishHide, 400);
        sync();
        if (focus) {
            document.querySelector("[data-category-jump]")?.focus({ preventScroll: true });
            announce(dirty() ? "Draft kept. Use Resume draft to continue." : "Category editor closed.");
        }
    };
    const open = ({ errors = false, reveal = false } = {}) => {
        if (!panel) return;
        window.clearTimeout(closeTimer);
        window.yoruMenuDrawerLayout?.activate(panel);
        panel.inert = false; panel.setAttribute("aria-hidden", "false");
        panel.getBoundingClientRect(); panel.classList.add("is-open"); engaged = true; sync();
        const summary = panel.querySelector(".validation-summary-errors:not([hidden])");
        const target = errors && summary ? summary : name();
        if (target === summary) summary.setAttribute("tabindex", "-1");
        target?.focus({ preventScroll: true });
        if (reveal) window.yoruMenuDrawerLayout?.reveal(panel);
    };
    const clearEditContext = () => {
        const url = new URL(window.location.href); url.searchParams.delete("editId");
        if (url.hash === "#category-editor") url.hash = "";
        window.history.replaceState(window.history.state, "", url);
        document.querySelectorAll('[data-category-workspace] input[name="editId"]').forEach(node => node.remove());
        document.querySelectorAll("[data-category-workspace] a[data-category-keep-scroll]:not([data-category-edit])").forEach(link => {
            const next = new URL(link.href, window.location.href);
            if (next.origin === url.origin && next.pathname === url.pathname) {
                next.searchParams.delete("editId"); link.href = next.href;
            }
        });
    };
    const reset = () => {
        if (!form()) return;
        const current = form();
        const jq = typeof window.jQuery === "function" ? window.jQuery(current) : null;
        if (jq && typeof jq.validate === "function") jq.validate().resetForm();
        current.reset();
        current.querySelector('[name="Input.Id"]').value = "";
        name().value = "";
        panel.dataset.savedCode = "";
        current.querySelectorAll("[data-valmsg-for]").forEach(node => { node.textContent = ""; });
        current.querySelectorAll(".validation-summary-errors, .validation-summary-valid").forEach(node => {
            node.textContent = ""; node.hidden = true; node.classList.remove("validation-summary-errors"); node.classList.add("validation-summary-valid");
        });
        name().classList.remove("input-validation-error"); name().removeAttribute("aria-invalid"); name().setCustomValidity("");
        text(panel.querySelector("#editor-title"), "New category");
        text(panel.querySelector("[data-category-save]"), "Create category");
        text(panel.querySelector("[data-category-drawer-description]"), "Name the next section of your menu.");
        forcedDirty = false; engaged = false; baseline = signature();
        clearEditContext(); sync();
    };
    const confirm = (title, { closing = false, discardLabel = "Discard changes" } = {}) => {
        if (pending) return Promise.resolve("cancel");
        let dialog = document.getElementById("category-draft-dialog");
        if (!dialog) {
            dialog = document.createElement("dialog"); dialog.id = "category-draft-dialog";
            dialog.className = "yoru-item-discard-dialog rounded-4 p-4";
            dialog.setAttribute("aria-labelledby", "category-draft-title");
            dialog.innerHTML = '<div class="d-flex justify-content-between gap-3 mb-4"><h2 id="category-draft-title" class="h4 mb-0"></h2>' +
                '<button type="button" class="btn-close" aria-label="Continue editing" data-category-choice="cancel"></button></div>' +
                '<div class="d-grid gap-2"><button type="button" class="btn btn-outline-secondary rounded-pill" data-category-choice="keep" autofocus></button>' +
                '<button type="button" class="btn btn-primary rounded-pill" data-category-choice="discard"></button></div>';
            dialog.addEventListener("click", event => {
                const action = event.target.closest("[data-category-choice]");
                if (action) dialog.close(action.dataset.categoryChoice);
            });
            document.body.append(dialog);
        }
        text(dialog.querySelector("#category-draft-title"), title);
        text(dialog.querySelector('[data-category-choice="keep"]'), closing ? "Keep draft & hide" : "Keep editing");
        text(dialog.querySelector('[data-category-choice="discard"]'), discardLabel);
        if (typeof dialog.showModal !== "function") {
            if (closing && window.confirm("Keep this category draft and hide the editor?")) return Promise.resolve("keep");
            return Promise.resolve(window.confirm(title) ? "discard" : "cancel");
        }
        return new Promise(resolve => {
            pending = resolve; dialog.returnValue = "";
            dialog.addEventListener("close", () => {
                const choice = ["keep", "discard"].includes(dialog.returnValue) ? dialog.returnValue : "cancel";
                pending = null; resolve(choice);
            }, { once: true });
            dialog.showModal();
        });
    };
    const close = async (discarding = false) => {
        if (!panel || busy || pending) return;
        const current = form();
        const action = dirty() ? await confirm(discarding ? "Discard this category draft?" : "Close this draft?",
            { closing: !discarding, discardLabel: discarding ? "Discard draft" : "Discard & close" }) : "discard";
        if (busy || form() !== current) return;
        if (action === "discard") { reset(); hide(); }
        else if (!discarding && action === "keep") hide();
    };
    const mount = (incoming, errors = false) => {
        const old = panel; panel = incoming;
        window.yoruMenuDrawerLayout?.replace(old, panel);
        panel.classList.add("is-enhanced"); panel.classList.remove("is-open");
        panel.setAttribute("role", "dialog"); panel.setAttribute("aria-modal", "false");
        panel.setAttribute("aria-hidden", "true"); panel.inert = true;
        panel.querySelectorAll("[data-category-drawer-close], [data-category-drawer-discard]").forEach(button => { button.hidden = false; });
        const slot = panel.parentElement;
        (document.querySelector("[data-menu-editor-host]") || document.body).append(panel);
        if (slot?.hasAttribute("data-category-drawer-slot")) slot.remove();
        old?.remove(); forcedDirty = errors; engaged = errors || !!editId(); baseline = signature();
        window.jQuery?.validator?.unobtrusive?.parse?.(form()); sync();
    };
    window.yoruCategoryDrawer = {
        get form() { return form(); },
        get isOpen() { return isOpen(); },
        get isDirty() { return dirty(); },
        confirm,
        setBusy(value) { busy = value; sync(); },
        async guardRequest({ destination, method, trigger }) {
            if (method === "GET" && trigger?.hasAttribute("data-category-edit") && dirty() &&
                destination.searchParams.get("editId") !== editId())
                return await confirm("Switch category?", { discardLabel: "Discard & switch" }) === "discard";
            return true;
        },
        receiveResponse({ replacement, method, trigger, redirected, notices }) {
            const incoming = replacement.querySelector("[data-category-drawer]");
            if (!incoming) {
                if (editId() && !dirty()) reset();
                hide(false); finishHide(); return { focus: false };
            }
            const slot = incoming.parentElement; incoming.remove();
            if (slot?.hasAttribute("data-category-drawer-slot")) slot.remove();
            const saving = method === "POST" && trigger === form();
            if (saving && redirected && notices.some(notice => notice.level === "success")) {
                mount(incoming); hide(false); return { focus: false };
            }
            if (saving) { mount(incoming, true); return { focus: true, errors: true }; }
            const directEdit = method === "GET" && trigger?.hasAttribute("data-category-edit");
            if (directEdit || !panel || (!dirty() && !engaged)) {
                const errors = incoming.dataset.categoryErrors === "true";
                mount(incoming, errors); return { focus: !!editId() || errors, errors };
            }
            // Refresh only route state and token; the actual draft input stays mounted.
            incoming.querySelectorAll('input[type="hidden"]').forEach(field => {
                if (field.name.startsWith("Input.")) return;
                const target = [...form().querySelectorAll('input[type="hidden"]')].find(node => node.name === field.name);
                if (target) target.value = field.value;
            });
            return { focus: false };
        },
        afterUpdate(result) {
            sync();
            if (result?.focus || result?.guide && document.querySelector('[data-category-workspace][data-menu-view="categories"]') && !document.querySelector("[data-category-order-form]")) open({ errors: result.errors, reveal: !!result.guide });
            else if (isOpen()) window.yoruMenuDrawerLayout?.activate(panel);
        }
    };
    document.addEventListener("click", event => {
        const action = event.target.closest?.("[data-category-jump], [data-category-drawer-close], [data-category-drawer-discard], [data-category-edit]");
        if (!action || event.defaultPrevented || event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
        if (action.hasAttribute("data-category-jump") && panel && document.querySelector('[data-category-workspace][data-menu-view="categories"]') &&
            !document.querySelector("[data-category-order-form]")) {
            event.preventDefault(); if (!busy) open({ reveal: true });
        } else if (action.hasAttribute("data-category-drawer-close") || action.hasAttribute("data-category-drawer-discard")) {
            event.preventDefault(); void close(action.hasAttribute("data-category-drawer-discard"));
        } else if (action.hasAttribute("data-category-edit") && panel && new URL(action.href, window.location.href).searchParams.get("editId") === editId()) {
            event.preventDefault(); if (!busy) open();
        }
    }, true);
    document.addEventListener("keydown", event => {
        if (event.key === "Escape" && isOpen() && panel.contains(event.target) && !pending) { event.preventDefault(); void close(); }
    });
    ["input", "change"].forEach(type => document.addEventListener(type, event => { if (form()?.contains(event.target)) sync(); }));
    document.addEventListener("yoru:drawer-activated", sync);
    document.addEventListener("transitionend", event => { if (event.target === panel && event.propertyName === "transform") finishHide(); });
    const initial = document.querySelector("[data-category-drawer]");
    if (initial) {
        const errors = initial.dataset.categoryErrors === "true";
        mount(initial, errors);
        if (editId() || errors || window.location.hash === "#category-editor") open({ errors });
    }
})();
