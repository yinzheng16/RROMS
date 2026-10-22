// Owner: Member 3. One persistent editor; list responses never destroy its draft.
(() => {
    "use strict";
    if (typeof window.fetch !== "function" || typeof window.DOMParser !== "function" ||
        typeof window.FormData !== "function") return;
    let panel = null, baseline = "", forcedDirty = false, engaged = false;
    let busy = false, lastField = null, warningAttached = false, pendingConfirmation = null;
    let closeLayoutTimer = null;
    let placementQueued = false;
    const studio = () => document.querySelector("[data-menu-studio]");
    const pageFooter = () => [...document.querySelectorAll('footer, [role="contentinfo"]')]
        .find(footer => !studio()?.contains(footer));
    const desktopLayout = window.matchMedia?.("(min-width: 87.5rem)");
    const scrollPane = () => window.yoruMenuDrawerLayout?.scrollPane;
    const placeEditor = () => window.yoruMenuDrawerLayout?.refresh();
    const queuePlacement = placeEditor;
    const dock = () => {
        window.clearTimeout(closeLayoutTimer);
        closeLayoutTimer = null;
        window.yoruMenuDrawerLayout?.activate(panel);
    };
    const finishHide = () => {
        if (!isOpen()) window.yoruMenuDrawerLayout?.deactivate(panel);
    };
    const form = () => panel?.querySelector("[data-menu-item-form]");
    const editId = () => form()?.querySelector('[name="ItemInput.Id"]')?.value || "";
    const text = (node, value) => { if (node && node.textContent !== value) node.textContent = value; };
    const signature = () => JSON.stringify([...form()?.elements || []]
        .filter(field => (field.name?.startsWith("ItemInput.") || field.name === "RemoveItemImage") &&
            (field.type !== "hidden" || field.name === "ItemInput.Id"))
        .map(field => [field.name, field.type === "checkbox" ? field.checked : field.value]));
    const dirty = () => !!panel && (forcedDirty || signature() !== baseline ||
        !!form()?.querySelector("[data-menu-image]")?.files?.length);
    const isOpen = () => !!panel?.classList.contains("is-open");
    const warnBeforeLeave = event => {
        if (!dirty()) return;
        event.preventDefault();
        event.returnValue = true;
    };
    const announce = message => {
        let status = document.getElementById("menu-drawer-live");
        if (!status) {
            status = document.createElement("span");
            status.id = "menu-drawer-live";
            status.className = "visually-hidden";
            status.setAttribute("role", "status");
            status.setAttribute("aria-live", "polite");
            document.body.append(status);
        }
        text(status, message);
    };
    const sync = () => {
        const activeId = isOpen() ? editId() : "";
        document.querySelectorAll("[data-menu-item-row]").forEach(row => {
            const active = !!activeId && row.dataset.menuItemRow === activeId;
            row.classList.toggle("table-active", active);
            const label = row.querySelector("[data-menu-editing-label]");
            if (label) label.hidden = !active;
        });
        const changed = dirty();
        text(panel?.querySelector("[data-editor-code]"), editId()
            ? panel.dataset.savedCode || "Unavailable — refresh the list"
            : "Assigned when saved");
        const codeNote = panel?.querySelector("[data-editor-code-note]");
        const selectedCategory = form()?.querySelector('[name="ItemInput.CategoryId"]')?.value;
        if (codeNote) codeNote.hidden = !(editId() && selectedCategory &&
            panel.dataset.savedCategoryId && selectedCategory !== panel.dataset.savedCategoryId);
        if (changed !== warningAttached) {
            window[changed ? "addEventListener" : "removeEventListener"]("beforeunload", warnBeforeLeave);
            warningAttached = changed;
        }
        text(panel?.querySelector("[data-drawer-state]"), busy ? "Updating menu…"
            : changed ? "Unsaved changes · kept while hidden"
            : editId() ? "Editing existing details" : "Ready for your next dish");
        const resume = !!panel && (changed || !!editId());
        document.querySelectorAll("[data-menu-new-item]").forEach(link => {
            const caption = link.querySelector("[data-menu-create-caption]");
            if (caption?.lastElementChild) {
                text(caption.lastElementChild, resume ? "Resume draft" : "New menu item");
                if (caption.firstElementChild !== caption.lastElementChild) caption.firstElementChild.hidden = resume;
            }
            link.setAttribute("aria-label", resume ? "Resume menu item draft" : "Create a new menu item");
        });
        const controls = document.querySelector("[data-menu-hero-controls]");
        let otherSectionResume = controls?.querySelector("[data-menu-resume-draft]");
        if (resume && controls && !controls.querySelector("[data-menu-new-item]")) {
            if (!otherSectionResume) {
                otherSectionResume = document.createElement("button");
                otherSectionResume.type = "button";
                otherSectionResume.className = "btn btn-outline-light rounded-pill";
                otherSectionResume.setAttribute("data-menu-resume-draft", "");
                otherSectionResume.textContent = "Resume menu item draft";
                controls.append(otherSectionResume);
            }
        } else otherSectionResume?.remove();
    };
    const open = ({ errors = false, reveal = false } = {}) => {
        if (!panel) return;
        dock();
        engaged = true;
        panel.inert = false;
        panel.setAttribute("aria-hidden", "false");
        // Establish the closed position before the first animated entrance.
        if (!isOpen()) panel.getBoundingClientRect();
        panel.classList.add("is-open");
        sync();
        const summary = panel.querySelector(".validation-summary-errors:not([hidden])");
        const invalid = [...form()?.elements || []].find(field => field.type !== "hidden" &&
            (field.classList.contains("input-validation-error") || field.getAttribute("aria-invalid") === "true")) ||
            [...form()?.querySelectorAll("[data-valmsg-for]") || []].filter(message => message.textContent.trim())
                .map(message => [...form().elements].find(field => field.name === message.dataset.valmsgFor && field.type !== "hidden"))
                .find(Boolean);
        let target = errors && (summary || invalid) ? summary || invalid
            : lastField?.isConnected && form()?.contains(lastField) ? lastField
            : form()?.querySelector("[data-category-name]");
        const focusTarget = target?.matches('select[name="ItemInput.CategoryId"][hidden]')
            ? target.closest("[data-menu-category-root]")?.querySelector(".yoru-menu-category-toggle") || target
            : target;
        if (summary && target === summary) {
            summary.setAttribute("tabindex", "-1");
            const body = panel.querySelector(".yoru-item-drawer-body");
            if (body) body.scrollTop = 0;
        } else if (errors && target === invalid) {
            const body = panel.querySelector(".yoru-item-drawer-body");
            if (body) {
                const fieldBox = focusTarget.getBoundingClientRect();
                body.scrollTop = Math.max(0, body.scrollTop + fieldBox.top -
                    body.getBoundingClientRect().top - fieldBox.height);
            }
        }
        focusTarget?.focus({ preventScroll: true });
        if (reveal) window.yoruMenuDrawerLayout?.reveal(panel);
    };
    const hide = ({ focus = true } = {}) => {
        if (!panel) return;
        panel.classList.remove("is-open");
        panel.inert = true;
        panel.setAttribute("aria-hidden", "true");
        window.clearTimeout(closeLayoutTimer);
        if (window.matchMedia?.("(prefers-reduced-motion: reduce)").matches) finishHide();
        else closeLayoutTimer = window.setTimeout(finishHide, 400);
        sync();
        if (focus) {
            announce(dirty() ? "Draft kept. Use Resume draft to continue." : "Menu item editor hidden.");
            (document.querySelector("[data-menu-resume-draft]") ||
                document.querySelector("[data-menu-new-item]") ||
                document.getElementById("menu-create"))?.focus({ preventScroll: true });
        }
    };
    const mount = (incoming, { errors = false } = {}) => {
        const old = panel;
        panel = incoming;
        window.yoruMenuDrawerLayout?.replace(old, panel);
        panel.classList.add("is-enhanced");
        panel.classList.remove("is-open");
        panel.setAttribute("role", "dialog");
        panel.setAttribute("aria-modal", "false");
        panel.setAttribute("aria-hidden", "true");
        panel.inert = true;
        panel.querySelectorAll("[data-drawer-hide], [data-drawer-discard]").forEach(button => { button.hidden = false; });
        const slot = panel.parentElement;
        (document.querySelector("[data-menu-editor-host]") || document.body).append(panel);
        if (slot?.hasAttribute("data-menu-drawer-slot")) slot.remove();
        old?.remove();
        lastField = null;
        forcedDirty = errors;
        baseline = signature();
        engaged = errors || !!editId();
        const validator = window.jQuery?.validator?.unobtrusive;
        if (validator && typeof validator.parse === "function") validator.parse(form());
        sync();
    };
    const chooseDraftAction = ({ closing = false, discarding = false } = {}) => {
        if (pendingConfirmation) return Promise.resolve("cancel");
        let dialog = document.getElementById("menu-discard-dialog");
        if (!dialog) {
            dialog = document.createElement("dialog");
            dialog.id = "menu-discard-dialog";
            dialog.className = "yoru-item-discard-dialog rounded-4 p-4";
            dialog.setAttribute("aria-labelledby", "menu-discard-title");
            dialog.innerHTML = '<div class="d-flex align-items-start justify-content-between gap-3 mb-4">' +
                '<h2 id="menu-discard-title" class="h4 mb-0">Close this draft?</h2>' +
                '<button type="button" class="btn-close flex-shrink-0" aria-label="Continue editing" data-draft-dialog-cancel></button></div>' +
                '<div class="d-grid gap-2">' +
                '<button type="button" class="btn btn-outline-secondary rounded-pill" data-keep-draft autofocus>Keep draft</button>' +
                '<button type="button" class="btn btn-primary rounded-pill" data-confirm-discard>Discard draft</button></div>';
            dialog.querySelector("[data-draft-dialog-cancel]").addEventListener("click", () => dialog.close("cancel"));
            dialog.querySelector("[data-keep-draft]").addEventListener("click", () => dialog.close("keep"));
            dialog.querySelector("[data-confirm-discard]").addEventListener("click", () => dialog.close("discard"));
            document.body.append(dialog);
        }
        text(dialog.querySelector("#menu-discard-title"), closing ? "Close this draft?"
            : discarding ? "Discard this draft?" : "Switch menu item?");
        text(dialog.querySelector("[data-keep-draft]"), closing ? "Keep draft & hide" : "Keep editing");
        text(dialog.querySelector("[data-confirm-discard]"), closing ? "Discard & close"
            : discarding ? "Discard draft" : "Discard & switch");
        if (typeof dialog.showModal !== "function") {
            if (closing && window.confirm("Keep this draft and hide the editor?\nOK keeps it. Cancel shows the discard choice."))
                return Promise.resolve("keep");
            return Promise.resolve(window.confirm("Discard your unsaved menu item details and selected photo?\nCancel returns to editing.")
                ? "discard" : "cancel");
        }
        return new Promise(resolve => {
            pendingConfirmation = resolve;
            dialog.returnValue = "";
            dialog.addEventListener("close", () => {
                const action = ["keep", "discard"].includes(dialog.returnValue) ? dialog.returnValue : "cancel";
                pendingConfirmation = null;
                resolve(action);
            }, { once: true });
            dialog.showModal();
        });
    };
    const resetDraft = () => {
        if (!panel) return;
        const current = form();
        const jqueryForm = typeof window.jQuery === "function" ? window.jQuery(current) : null;
        if (jqueryForm && typeof jqueryForm.validate === "function") jqueryForm.validate().resetForm();
        current.reset();
        const set = (name, value) => {
            const field = current.querySelector('[name="' + name + '"]');
            if (field) field.value = value;
        };
        ["ItemInput.Id", "ItemInput.Name", "ItemInput.Description", "ItemInput.Price",
            "ItemInput.CategoryId"].forEach(name => set(name, ""));
        const available = current.querySelector('input[type="checkbox"][name="ItemInput.IsAvailable"]');
        if (available) available.checked = true;
        current.querySelector("[data-menu-remove-image]")?.closest(".form-check")?.remove();
        current.querySelectorAll("[data-current-image]").forEach(image => { image.dataset.currentImage = ""; });
        current.querySelectorAll("[data-valmsg-for], [asp-validation-for]").forEach(message => { message.textContent = ""; });
        current.querySelectorAll(".validation-summary-errors, .validation-summary-valid").forEach(summary => {
            summary.textContent = "";
            summary.classList.remove("validation-summary-errors");
            summary.classList.add("validation-summary-valid");
        });
        current.querySelector("[data-menu-photo-reselect]")?.remove();
        current.querySelectorAll("input, select, textarea").forEach(field => {
            field.setCustomValidity?.("");
            field.classList.remove("input-validation-error");
            field.removeAttribute("aria-invalid");
        });
        text(panel.querySelector("#item-editor-title"), "New menu item");
        text(panel.querySelector("[data-item-save]"), "Create item");
        panel.dataset.itemErrors = "false";
        panel.dataset.savedCode = "";
        panel.dataset.savedCategoryId = "";
        forcedDirty = false;
        engaged = false;
        lastField = null;
        baseline = signature();
        current.dispatchEvent(new window.CustomEvent("yoru:item-form-reset"));
        const url = new URL(window.location.href);
        url.searchParams.delete("itemEditId");
        if (url.hash === "#menu-item-drawer") url.hash = "";
        window.history.replaceState(window.history.state, "", url);
        // Pagination/filter links were rendered while an edit was selected.
        // Clear only that stale context; each row's explicit Edit link keeps its ID.
        document.querySelectorAll('[data-category-workspace] input[name="itemEditId"]').forEach(field => field.remove());
        document.querySelectorAll("[data-category-workspace] a[data-category-keep-scroll]:not([data-menu-edit])").forEach(link => {
            const destination = new URL(link.href, window.location.href);
            if (destination.origin === window.location.origin && destination.pathname === window.location.pathname &&
                destination.searchParams.has("itemEditId")) {
                destination.searchParams.delete("itemEditId");
                link.href = destination.href;
            }
        });
        sync();
    };
    const requestClose = async () => {
        if (!panel || busy || pendingConfirmation) return;
        if (!dirty()) {
            // Finishing an unchanged edit must leave New available now that
            // there is no separate Discard action to clear the edit context.
            if (editId()) resetDraft();
            hide();
            return;
        }
        const current = form();
        const action = await chooseDraftAction({ closing: true });
        // An asynchronous response may have replaced the form while the dialog
        // was open. A choice must never clear a different draft or an active save.
        if (form() !== current || busy) return;
        if (action === "discard") {
            resetDraft();
            hide();
            announce("Menu item draft discarded.");
        } else if (action === "keep") hide();
    };
    const copyViewState = incoming => {
        const target = form();
        incoming.querySelectorAll('input[type="hidden"]').forEach(field => {
            if (field.name.startsWith("ItemInput.") || field.name === "RemoveItemImage") return;
            const existing = [...target.querySelectorAll('input[type="hidden"]')].find(node => node.name === field.name);
            if (existing) existing.value = field.value;
        });
        const categories = target.querySelector('[name="ItemInput.CategoryId"]');
        const freshCategories = incoming.querySelector('[name="ItemInput.CategoryId"]');
        if (categories && freshCategories) {
            const selected = categories.value;
            categories.replaceChildren(...[...freshCategories.options].map(option => option.cloneNode(true)));
            categories.value = selected;
            if (selected && categories.value !== selected) {
                categories.value = "";
                forcedDirty = true;
                announce("The selected category no longer exists. Choose another category before saving.");
            }
        }
        const save = panel.querySelector("[data-item-save]");
        const freshSave = incoming.querySelector("[data-item-save]");
        if (save && freshSave) save.disabled = freshSave.disabled;
        const oldNotice = panel.querySelector("[data-menu-no-categories]");
        const freshNotice = incoming.querySelector("[data-menu-no-categories]");
        oldNotice?.remove();
        if (freshNotice) target.before(freshNotice.cloneNode(true));
    };
    window.yoruMenuDrawer = {
        get form() { return form(); },
        get scrollPane() { return scrollPane(); },
        get isOpen() { return isOpen(); },
        get isDirty() { return dirty(); },
        setBusy(value) { busy = value; sync(); },
        async guardRequest({ destination, method, trigger }) {
            const requestedId = destination.searchParams.get("itemEditId");
            if (method === "GET" && trigger?.hasAttribute("data-menu-edit") &&
                panel && dirty() && requestedId !== editId()) return await chooseDraftAction() === "discard";
            return true;
        },
        receiveResponse({ replacement, method, trigger, redirected, notices, url }) {
            const incoming = replacement.querySelector("[data-menu-drawer]");
            if (!incoming) {
                if (editId() && !dirty()) resetDraft();
                hide({ focus: false }); finishHide(); return { focus: false };
            }
            const slot = incoming.parentElement;
            incoming.remove();
            if (slot?.hasAttribute("data-menu-drawer-slot")) slot.remove();
            const saving = method === "POST" && trigger === form();
            const success = saving && redirected && notices.some(notice => notice.level === "success");
            if (success) {
                mount(incoming);
                hide({ focus: false });
                return { focus: false };
            }
            if (saving) {
                // Preserve the actual File node on a validation response, too.
                const selected = form()?.querySelector("[data-menu-image]");
                if (selected?.files?.length) {
                    incoming.querySelector("[data-menu-image]")?.replaceWith(selected);
                    incoming.querySelector("[data-menu-photo-reselect]")?.remove();
                }
                mount(incoming, { errors: true });
                return { focus: true, errors: true };
            }
            const directEdit = method === "GET" && trigger?.hasAttribute("data-menu-edit");
            const incomingId = incoming.querySelector('[name="ItemInput.Id"]')?.value || "";
            if (directEdit || !panel || (!dirty() && !engaged)) {
                mount(incoming, { errors: incoming.dataset.itemErrors === "true" });
                return { focus: !!incomingId || forcedDirty, errors: forcedDirty };
            }
            copyViewState(incoming);
            // Browser history may point to a different edit page. Keep the draft;
            // do not replace it with historical form contents.
            if (url.searchParams.get("itemEditId") && incomingId !== editId()) hide({ focus: false });
            return { focus: false };
        },
        afterUpdate(result) {
            sync();
            if (result?.focus) open({ errors: result.errors });
            else if (isOpen()) dock();
        }
    };
    const ordinaryClick = (event, link) => !event.defaultPrevented && event.button === 0 &&
        !event.ctrlKey && !event.metaKey && !event.shiftKey && !event.altKey &&
        (!link.target || link.target === "_self");
    document.addEventListener("click", async event => {
        const action = event.target.closest?.("[data-menu-new-item], [data-menu-resume-draft], [data-drawer-hide], [data-drawer-discard], a[data-menu-edit]");
        if (!action || !ordinaryClick(event, action)) return;
        if (action.hasAttribute("data-menu-new-item") || action.hasAttribute("data-menu-resume-draft")) {
            if (!panel) return; // Let normal navigation load an initial form.
            event.preventDefault();
            open({ reveal: true });
        } else if (action.hasAttribute("data-drawer-hide")) {
            event.preventDefault();
            await requestClose();
        } else if (action.hasAttribute("data-drawer-discard")) {
            event.preventDefault();
            if (busy || pendingConfirmation) return;
            const current = form();
            if (dirty() && await chooseDraftAction({ discarding: true }) !== "discard") return;
            if (busy || form() !== current) return;
            resetDraft();
            hide();
            announce("Menu item draft discarded.");
        } else if (panel && new URL(action.href, window.location.href).searchParams.get("itemEditId") === editId()) {
            event.preventDefault();
            open();
        }
    }, true);
    document.addEventListener("yoru:drawer-activated", sync);
    document.addEventListener("transitionend", event => {
        if (event.target === panel && event.propertyName === "transform") finishHide();
    });
    window.addEventListener("resize", placeEditor);
    window.addEventListener("scroll", queuePlacement, { passive: true });
    window.visualViewport?.addEventListener("resize", placeEditor);
    window.visualViewport?.addEventListener("scroll", queuePlacement, { passive: true });
    desktopLayout?.addEventListener("change", placeEditor);
    if (typeof window.ResizeObserver === "function") {
        const observer = new window.ResizeObserver(placeEditor);
        [document.querySelector("[data-menu-editor-host]"),
            document.querySelector("[data-menu-content]"),
            document.querySelector("body > header"), pageFooter()]
            .filter(Boolean).forEach(element => observer.observe(element));
    }
    window.addEventListener("pageshow", () => { if (isOpen()) placeEditor(); });
    document.addEventListener("keydown", event => {
        if (event.key === "Escape" && isOpen() && !pendingConfirmation && panel.contains(event.target)) {
            event.preventDefault();
            void requestClose();
        }
    });
    document.addEventListener("focusin", event => {
        if (form()?.contains(event.target)) lastField = event.target;
    });
    ["input", "change"].forEach(type => document.addEventListener(type, event => {
        if (form()?.contains(event.target)) sync();
    }));
    const initial = document.querySelector("[data-menu-drawer]");
    if (initial) {
        const errors = initial.dataset.itemErrors === "true";
        mount(initial, { errors });
        if (editId() || errors || window.location.hash === "#menu-item-drawer") open({ errors });
    }
})();
