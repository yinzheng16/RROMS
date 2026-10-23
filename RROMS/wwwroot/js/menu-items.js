(() => {
    "use strict";
    let dispose = () => { };
    let activeForm = null;
    let disposeThumbnails = () => { };
    let activeFilters = null, disposeFilters = () => { };
    let editorCategoryPicker = null;
    let photoDialog = null, photoTrigger = null, photoMotion = null, photoClosing = false;
    let restorePhotoFocus = true;
    let photoScale = 1, photoX = 0, photoY = 0, photoDrag = null;
    const renderPhotoTransform = () => {
        const image = photoDialog?.querySelector("[data-photo-full]");
        const surface = photoDialog?.querySelector("[data-photo-surface]");
        if (!image || !surface) return;
        const box = surface.getBoundingClientRect();
        const width = image.naturalWidth || box.width, height = image.naturalHeight || box.height;
        const fit = width > 0 && height > 0 ? Math.min(box.width / width, box.height / height) : 1;
        const limitX = Math.max(0, (width * fit * photoScale - box.width) / 2);
        const limitY = Math.max(0, (height * fit * photoScale - box.height) / 2);
        photoX = Math.max(-limitX, Math.min(photoX, limitX));
        photoY = Math.max(-limitY, Math.min(photoY, limitY));
        image.style.transform = "translate(" + photoX + "px, " + photoY + "px) scale(" + photoScale + ")";
        photoDialog.querySelector("[data-photo-percent]").textContent = Math.round(photoScale * 100) + "%";
        photoDialog.querySelector("[data-photo-minus]").disabled = image.hidden || photoScale <= 0.5;
        photoDialog.querySelector("[data-photo-plus]").disabled = image.hidden || photoScale >= 5;
        surface.classList.toggle("is-zoomed", photoScale > 1 && !image.hidden);
        surface.classList.toggle("is-dragging", !!photoDrag);
    };
    const fitPhoto = () => { photoScale = 1; photoX = photoY = 0; photoDrag = null; renderPhotoTransform(); };
    const zoomPhoto = (scale, point = null) => {
        if (!photoDialog?.open || photoClosing || photoDialog.querySelector("[data-photo-full]").hidden) return;
        const bounded = Math.max(0.5, Math.min(scale, 5));
        const next = Math.abs(bounded - 1) < 0.00001 ? 1 : bounded;
        const box = photoDialog.querySelector("[data-photo-surface]").getBoundingClientRect();
        const x = Number.isFinite(point?.x) ? point.x - box.left - box.width / 2 : 0;
        const y = Number.isFinite(point?.y) ? point.y - box.top - box.height / 2 : 0;
        const ratio = next / photoScale;
        photoX = x - (x - photoX) * ratio;
        photoY = y - (y - photoY) * ratio;
        photoScale = next; renderPhotoTransform();
    };
    const reducedMotion = window.matchMedia?.("(prefers-reduced-motion: reduce)");
    // Only category collection artwork keeps its short reveal. The hero and
    // dish collection stay static on first load, reload and AJAX navigation.
    const enteredBackgrounds = new Set();
    const backgroundJobs = new Map();
    const clearBackgroundMotion = () => {
        for (const dispose of [...backgroundJobs.values()]) dispose();
    };
    const initializeBackgrounds = () => {
        for (const [image, dispose] of backgroundJobs) {
            if (!image.isConnected) dispose();
        }
        for (const [key, selector] of [
            ["categories", ".yoru-category-collection-intro .yoru-menu-collection-photo"]
        ]) {
            const image = document.querySelector(selector);
            if (!image || enteredBackgrounds.has(key)) continue;
            enteredBackgrounds.add(key);
            if (reducedMotion?.matches || typeof image.animate !== "function") continue;
            const controller = new AbortController();
            let motion = null, disposed = false;
            const dispose = () => {
                if (disposed) return;
                disposed = true;
                controller.abort();
                if (motion) {
                    motion.onfinish = null; motion.oncancel = null; motion.cancel();
                }
                backgroundJobs.delete(image);
            };
            const play = () => {
                controller.abort();
                if (!image.isConnected || reducedMotion?.matches || !image.naturalWidth) {
                    dispose(); return;
                }
                try {
                    motion = image.animate([{ opacity: 0 }, { opacity: 1 }],
                        { duration: 500, easing: "ease-out" });
                    motion.onfinish = dispose;
                    motion.oncancel = dispose;
                } catch { dispose(); } // The static image remains usable without WAAPI.
            };
            backgroundJobs.set(image, dispose);
            if (image.complete) play();
            else {
                image.addEventListener("load", play, { once: true, signal: controller.signal });
                image.addEventListener("error", dispose, { once: true, signal: controller.signal });
            }
        }
    };
    reducedMotion?.addEventListener?.("change", () => {
        if (reducedMotion.matches) clearBackgroundMotion();
    });
    const setPhotoExpanded = expanded => {
        if (!photoDialog) return;
        photoDialog.classList.toggle("is-expanded", expanded);
        const button = photoDialog.querySelector("[data-photo-expand]");
        button.setAttribute("aria-pressed", String(expanded));
        button.setAttribute("aria-label", expanded ? "Restore viewer size" : "Expand viewer");
        button.title = expanded ? "Restore viewer size" : "Expand viewer";
        renderPhotoTransform();
    };
    const cleanPhotoView = () => {
        photoMotion?.cancel(); photoMotion = null; photoClosing = false;
        photoDrag = null;
        setPhotoExpanded(false);
        document.documentElement.classList.remove("yoru-photo-open");
        photoDialog?.querySelector("[data-photo-full]").removeAttribute("src");
        const trigger = photoTrigger; photoTrigger = null;
        if (restorePhotoFocus && trigger?.isConnected) trigger.focus({ preventScroll: true });
        restorePhotoFocus = true;
    };
    const closePhotoView = ({ immediate = false, focus = true } = {}) => {
        if (!photoDialog?.open) return;
        restorePhotoFocus = focus;
        if (photoClosing && !immediate) return;
        photoMotion?.cancel(); photoMotion = null;
        const finish = () => { photoDialog.close(); cleanPhotoView(); };
        if (immediate || reducedMotion?.matches || typeof photoDialog.animate !== "function") { finish(); return; }
        photoClosing = true;
        photoMotion = photoDialog.animate([
            { opacity: 1, transform: "none" },
            { opacity: 0, transform: "translateY(0.5rem) scale(0.97)" }
        ], { duration: 140, easing: "ease-in" });
        photoMotion.onfinish = finish;
    };
    const createPhotoView = () => {
        if (photoDialog) return photoDialog;
        const dialog = document.createElement("dialog");
        if (typeof dialog.showModal !== "function") return null;
        dialog.className = "yoru-photo-viewer";
        dialog.setAttribute("aria-labelledby", "menu-photo-title");
        dialog.innerHTML = '<div class="yoru-photo-header d-flex align-items-center justify-content-between gap-3 p-3">' +
            '<h2 id="menu-photo-title" class="h5 text-break mb-0">Dish photo</h2>' +
            '<button type="button" class="yoru-photo-close flex-shrink-0" aria-label="Close enlarged photo" data-photo-close autofocus>×</button></div>' +
            '<div class="yoru-photo-body">' +
            '<div class="yoru-photo-surface" data-photo-surface tabindex="0" role="region" aria-label="Dish photo. Scroll to zoom; drag to pan; use plus, minus or zero on the keyboard."><img class="yoru-photo-full" data-photo-full alt="" draggable="false">' +
            '<p class="m-0 text-body-secondary" role="status" data-photo-error hidden>This photo could not be displayed.</p></div>' +
            '<div class="yoru-photo-tools" role="group" aria-label="Photo zoom controls">' +
            '<button type="button" aria-label="Zoom out" title="Zoom out" data-photo-minus>−</button>' +
            '<output class="small text-body-secondary text-center" aria-label="Zoom relative to fitted photo" aria-live="off" data-photo-percent>100%</output>' +
            '<button type="button" aria-label="Zoom in" title="Zoom in" data-photo-plus>+</button>' +
            '<span class="yoru-photo-divider" aria-hidden="true"></span>' +
            '<button type="button" aria-label="Fit photo to viewer" title="Fit photo to viewer" data-photo-fit>Fit</button>' +
            '<button type="button" aria-label="Expand viewer" title="Expand viewer" aria-pressed="false" data-photo-expand>' +
            '<svg class="yoru-photo-expand-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.75" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false"><path d="M8 3H5a2 2 0 0 0-2 2v3m13-5h3a2 2 0 0 1 2 2v3M3 16v3a2 2 0 0 0 2 2h3m13-5v3a2 2 0 0 1-2 2h-3"/></svg>' +
            '<svg class="yoru-photo-restore-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.75" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false"><path d="M8 3v3a2 2 0 0 1-2 2H3m13-5v3a2 2 0 0 0 2 2h3M3 16h3a2 2 0 0 1 2 2v3m13-5h-3a2 2 0 0 0-2 2v3"/></svg>' +
            '</button></div></div>';
        const surface = dialog.querySelector("[data-photo-surface]");
        dialog.querySelector("[data-photo-plus]").addEventListener("click", () => zoomPhoto(photoScale * 1.2));
        dialog.querySelector("[data-photo-minus]").addEventListener("click", () => zoomPhoto(photoScale / 1.2));
        dialog.querySelector("[data-photo-fit]").addEventListener("click", fitPhoto);
        dialog.querySelector("[data-photo-expand]").addEventListener("click", () => {
            if (!photoClosing) setPhotoExpanded(!dialog.classList.contains("is-expanded"));
        });
        surface.addEventListener("wheel", event => {
            event.preventDefault();
            const delta = event.deltaY * (event.deltaMode === 1 ? 16 : event.deltaMode === 2 ? surface.clientHeight : 1);
            zoomPhoto(photoScale * Math.exp(-Math.max(-100, Math.min(delta, 100)) * 0.002),
                { x: event.clientX, y: event.clientY });
        }, { passive: false });
        surface.addEventListener("pointerdown", event => {
            if (event.button !== 0 || photoScale <= 1 || photoClosing) return;
            photoDrag = { id: event.pointerId, x: event.clientX, y: event.clientY };
            try { surface.setPointerCapture(event.pointerId); } catch { /* Pointer capture is optional. */ }
            event.preventDefault(); renderPhotoTransform();
        });
        surface.addEventListener("pointermove", event => {
            if (!photoDrag || event.pointerId !== photoDrag.id) return;
            photoX += event.clientX - photoDrag.x; photoY += event.clientY - photoDrag.y;
            photoDrag.x = event.clientX; photoDrag.y = event.clientY;
            renderPhotoTransform();
        });
        const endDrag = event => {
            if (!photoDrag || event.pointerId !== photoDrag.id) return;
            photoDrag = null; renderPhotoTransform();
        };
        ["pointerup", "pointercancel", "lostpointercapture"].forEach(type => surface.addEventListener(type, endDrag));
        surface.addEventListener("keydown", event => {
            if (event.target !== surface) return;
            if (["+", "="].includes(event.key)) zoomPhoto(photoScale * 1.2);
            else if (["-", "_"].includes(event.key)) zoomPhoto(photoScale / 1.2);
            else if (["0", "Home"].includes(event.key)) fitPhoto();
            else if (photoScale > 1 && event.key.startsWith("Arrow")) {
                if (event.key === "ArrowLeft") photoX += 40;
                if (event.key === "ArrowRight") photoX -= 40;
                if (event.key === "ArrowUp") photoY += 40;
                if (event.key === "ArrowDown") photoY -= 40;
                renderPhotoTransform();
            } else return;
            event.preventDefault();
        });
        if (typeof window.ResizeObserver === "function") new window.ResizeObserver(renderPhotoTransform).observe(surface);
        window.addEventListener("resize", renderPhotoTransform);
        dialog.querySelector("[data-photo-full]").addEventListener("load", renderPhotoTransform);
        dialog.querySelector("[data-photo-close]").addEventListener("click", () => closePhotoView());
        dialog.addEventListener("cancel", event => { event.preventDefault(); closePhotoView(); });
        dialog.addEventListener("close", () => { if (!dialog.open) cleanPhotoView(); });
        let pressedBackdrop = false;
        const outside = event => {
            const box = dialog.getBoundingClientRect();
            return event.target === dialog && (event.clientX < box.left || event.clientX > box.right ||
                event.clientY < box.top || event.clientY > box.bottom);
        };
        dialog.addEventListener("pointerdown", event => { pressedBackdrop = outside(event); });
        dialog.addEventListener("click", event => {
            if (pressedBackdrop && outside(event)) closePhotoView();
            pressedBackdrop = false;
        });
        dialog.querySelector("[data-photo-full]").addEventListener("error", () => {
            if (!dialog.open || !dialog.querySelector("[data-photo-full]").hasAttribute("src")) return;
            dialog.querySelector("[data-photo-full]").hidden = true;
            dialog.querySelector("[data-photo-error]").hidden = false;
            fitPhoto();
        });
        document.body.append(dialog);
        photoDialog = dialog;
        return dialog;
    };
    document.addEventListener("click", event => {
        const link = event.target.closest?.("a[data-menu-photo-zoom]");
        if (!link || event.defaultPrevented || event.button !== 0 || event.ctrlKey || event.metaKey ||
            event.shiftKey || event.altKey || link.hasAttribute("download") ||
            link.target && link.target !== "_self") return;
        const image = link.querySelector("img"), source = link.getAttribute("href");
        if (!source || !image || image.hidden || link.hidden) return;
        const dialog = createPhotoView();
        if (!dialog) return; // The image link is the native fallback.
        const name = link.dataset.photoName?.trim() || "Dish photo";
        dialog.querySelector("#menu-photo-title").textContent = name;
        const full = dialog.querySelector("[data-photo-full]");
        full.alt = name; full.hidden = false; full.src = source;
        dialog.querySelector("[data-photo-error]").hidden = true;
        try { dialog.showModal(); } catch { return; }
        fitPhoto();
        event.preventDefault();
        photoTrigger = link; photoClosing = false; restorePhotoFocus = true;
        document.documentElement.classList.add("yoru-photo-open");
        photoMotion?.cancel(); photoMotion = null;
        if (!reducedMotion?.matches && typeof dialog.animate === "function") {
            photoMotion = dialog.animate([
                { opacity: 0, transform: "translateY(0.75rem) scale(0.94)" },
                { opacity: 1, transform: "none" }
            ], { duration: 240, easing: "cubic-bezier(0.22, 1, 0.36, 1)" });
        }
        dialog.querySelector("[data-photo-close]").focus({ preventScroll: true });
    });
    reducedMotion?.addEventListener?.("change", () => {
        if (!reducedMotion.matches) return;
        if (photoClosing) closePhotoView({ immediate: true });
        else { photoMotion?.cancel(); photoMotion = null; }
    });
    const initializeThumbnails = () => {
        disposeThumbnails();
        const controller = new AbortController();
        document.querySelectorAll("[data-menu-thumbnail]").forEach(thumbnail => {
            const fallbackPhoto = () => {
                if (!thumbnail.isConnected) return;
                const fallback = document.createElement("span");
                fallback.className = "d-flex align-items-center justify-content-center small text-body-secondary text-center";
                fallback.textContent = "No photo";
                (thumbnail.closest("[data-menu-photo-zoom]") || thumbnail).replaceWith(fallback);
            };
            thumbnail.addEventListener("error", fallbackPhoto, { signal: controller.signal });
            if (thumbnail.complete && thumbnail.naturalWidth === 0) fallbackPhoto();
        });
        disposeThumbnails = () => controller.abort();
    };
    const createCategoryPicker = (form, on, fieldName = "ItemCategory", prefix = "menu-category") => {
        const select = [...form.querySelectorAll("select")].find(field => field.name === fieldName);
        const host = select?.closest("[data-menu-category-root]");
        if (!host) return null;
        const label = host.querySelector("label");
        const originalHidden = select.hidden, originalFor = label?.getAttribute("for");
        const wasFocused = document.activeElement === select;
        const validation = host.querySelector("[data-valmsg-for]");
        const locked = () => select.disabled || select.inert;
        const mount = document.createElement("div");
        mount.className = "yoru-menu-category-picker";
        const trigger = document.createElement("button");
        trigger.type = "button"; trigger.id = prefix + "-toggle";
        trigger.className = "btn rounded-3 yoru-menu-category-toggle";
        trigger.setAttribute("aria-expanded", "false");
        trigger.setAttribute("aria-controls", prefix + "-options");
        if (select.required) trigger.setAttribute("aria-required", "true");
        if (validation?.id) trigger.setAttribute("aria-describedby", validation.id);
        const caption = document.createElement("span"); caption.id = prefix + "-value";
        const chevron = document.createElement("span"); chevron.setAttribute("aria-hidden", "true");
        chevron.innerHTML = '<svg width="1em" height="1em" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.75"><path d="m6 9 6 6 6-6" stroke-linecap="round" stroke-linejoin="round"/></svg>';
        trigger.append(caption, chevron);
        if (label?.id) trigger.setAttribute("aria-labelledby", label.id + " " + caption.id);
        else trigger.setAttribute("aria-label", "Choose a category");
        const popup = document.createElement("div"); popup.id = prefix + "-options";
        popup.className = "yoru-menu-category-popup border rounded-3 p-2 mt-2"; popup.hidden = true;
        const searchBox = document.createElement("div");
        searchBox.className = "yoru-menu-category-search d-flex align-items-center gap-2 border rounded-2 px-2 mb-2";
        const searchIcon = document.createElement("span"); searchIcon.setAttribute("aria-hidden", "true");
        searchIcon.innerHTML = '<svg width="1em" height="1em" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.75"><circle cx="10.5" cy="10.5" r="6.5"/><path d="m16 16 4 4" stroke-linecap="round"/></svg>';
        const query = document.createElement("input");
        query.type = "search"; query.id = prefix + "-search"; query.maxLength = 100;
        query.autocomplete = "off"; query.placeholder = "Search categories…";
        query.className = "form-control border-0";
        query.setAttribute("aria-label", "Search categories");
        // No name: searching this local list must never become a server filter.
        searchBox.append(searchIcon, query);
        const list = document.createElement("div"); list.className = "yoru-menu-category-options";
        list.setAttribute("role", "group"); list.setAttribute("aria-label", "Category choices");
        const status = document.createElement("p"); status.className = "small text-body-secondary m-2";
        status.setAttribute("role", "status"); status.hidden = true; status.textContent = "No matching categories";
        const close = (focus = false) => {
            popup.hidden = true; trigger.setAttribute("aria-expanded", "false");
            if (focus) trigger.focus({ preventScroll: true });
        };
        let options = [], optionSignature = "";
        const refreshOptions = () => {
            const next = JSON.stringify([...select.options].map(option => [option.value, option.textContent, option.disabled]));
            if (next === optionSignature) return;
            optionSignature = next; list.replaceChildren();
            options = [...select.options].map(option => {
                const button = document.createElement("button"); button.type = "button";
                button.className = "btn rounded-2 yoru-menu-category-option";
                button.dataset.categoryValue = option.value; button.disabled = option.disabled;
                const text = document.createElement("span"); text.textContent = option.textContent.trim();
                const check = document.createElement("span"); check.className = "yoru-menu-category-check";
                check.setAttribute("aria-hidden", "true"); check.textContent = "✓";
                button.append(text, check); list.append(button);
                return { button, value: option.value, text: text.textContent.toLowerCase() };
            });
        };
        const sync = () => {
            refreshOptions();
            caption.textContent = select.selectedOptions[0]?.textContent.trim() || (select.required ? "Select a category" : "All categories");
            trigger.disabled = select.disabled;
            trigger.setAttribute("aria-invalid", String(select.getAttribute("aria-invalid") === "true" || select.classList.contains("input-validation-error")));
            options.forEach(({ button, value }) => button.setAttribute("aria-pressed", String(value === select.value)));
        };
        on(list, "click", event => {
            const button = event.target.closest?.("[data-category-value]");
            if (!button || !list.contains(button) || button.disabled || locked()) return;
            select.value = button.dataset.categoryValue;
            if (select.validity.valid) {
                select.classList.remove("input-validation-error"); select.removeAttribute("aria-invalid");
                if (validation) {
                    validation.textContent = "";
                    validation.classList.remove("field-validation-error"); validation.classList.add("field-validation-valid");
                }
            }
            sync(); close(true);
            select.dispatchEvent(new window.Event("change", { bubbles: true }));
        });
        const filter = () => {
            const term = query.value.trim().toLowerCase();
            options.forEach(({ button, text }) => { button.hidden = !text.includes(term); });
            status.hidden = options.some(({ button }) => !button.hidden && !button.disabled);
        };
        const open = () => {
            if (locked()) return;
            query.value = ""; sync(); filter(); popup.hidden = false;
            trigger.setAttribute("aria-expanded", "true"); query.focus({ preventScroll: true });
        };
        on(trigger, "click", () => popup.hidden ? open() : close());
        on(trigger, "keydown", event => { if (event.key === "ArrowDown") { event.preventDefault(); open(); } });
        on(query, "input", filter);
        const visible = () => options.filter(({ button }) => !button.hidden && !button.disabled).map(({ button }) => button);
        const focusOption = button => { button?.focus({ preventScroll: true }); button?.scrollIntoView?.({ block: "nearest" }); };
        on(query, "keydown", event => {
            if (event.key === "Enter") {
                event.preventDefault(); event.stopPropagation();
                const matches = visible(); if (matches.length === 1) matches[0].click();
            } else if (event.key === "ArrowDown") { event.preventDefault(); focusOption(visible()[0]); }
        });
        on(list, "keydown", event => {
            if (!["ArrowDown", "ArrowUp", "Home", "End"].includes(event.key)) return;
            const buttons = visible(), index = buttons.indexOf(event.target);
            if (index < 0) return;
            event.preventDefault();
            const next = event.key === "Home" ? 0 : event.key === "End" ? buttons.length - 1
                : (index + (event.key === "ArrowDown" ? 1 : -1) + buttons.length) % buttons.length;
            focusOption(buttons[next]);
        });
        on(popup, "keydown", event => {
            if (event.key === "Escape") { event.preventDefault(); event.stopPropagation(); close(true); }
        });
        on(document, "click", event => { if (!host.contains(event.target)) close(); });
        on(select, "change", sync);
        on(select, "invalid", event => {
            event.preventDefault();
            select.setAttribute("aria-invalid", "true"); sync();
            if (validation) {
                validation.textContent = select.validationMessage || "Choose a category.";
                validation.classList.remove("field-validation-valid"); validation.classList.add("field-validation-error");
            }
            close(); trigger.focus({ preventScroll: true }); trigger.scrollIntoView?.({ block: "nearest" });
        });
        on(form, "reset", () => Promise.resolve().then(() => { if (mount.isConnected) { close(); sync(); } }));
        popup.append(searchBox, list, status); mount.append(trigger, popup); select.after(mount);
        select.hidden = true; if (label) label.setAttribute("for", trigger.id); sync();
        if (wasFocused) trigger.focus({ preventScroll: true });
        return {
            sync, close, trigger, dispose: () => {
                mount.remove(); select.hidden = originalHidden;
                if (label) { if (originalFor === null) label.removeAttribute("for"); else label.setAttribute("for", originalFor); }
            }
        };
    };
    const initializeFilters = () => {
        const root = document.querySelector("[data-menu-filter-root]");
        if (root && root === activeFilters) return;
        disposeFilters(); activeFilters = root;
        if (!root) { disposeFilters = () => { }; return; }
        const toggle = root.querySelector("[data-menu-filter-toggle]");
        const panel = root.querySelector("[data-menu-filter-panel]");
        const form = root.querySelector("[data-menu-filter-form]");
        const search = root.querySelector('[data-menu-search-form] [name="ItemSearch"], [data-menu-search-form] [name="SearchTerm"]');
        if (!toggle || !panel || !form) return; // Both GET forms remain usable without enhancement.
        const controller = new AbortController();
        const on = (node, type, callback, capture = false) => node?.addEventListener(type, callback,
            { signal: controller.signal, capture });
        // Guard only the explicit Search form. Filter Apply and Reset can still
        // show all dishes with an empty name, and no draft/list state is changed.
        const searchForm = root.querySelector("[data-menu-search-form]");
        if (searchForm && search) {
            let prompt = root.querySelector("[data-menu-search-prompt]");
            if (!prompt) {
                prompt = document.createElement("p");
                prompt.id = "menu-item-search-prompt";
                prompt.className = "mb-0";
                prompt.setAttribute("data-menu-search-prompt", "");
                prompt.setAttribute("role", "status");
                prompt.setAttribute("aria-live", "polite");
                prompt.hidden = true;
                (searchForm.closest(".yoru-menu-toolbar") || searchForm).after(prompt);
            }
            // Keep a measured hint slot in both Razor output and the fallback markup.
            // Revealing the message must not grow the header or rescale its cover photo.
            let feedback = prompt.closest("[data-menu-search-feedback]");
            if (!feedback) {
                feedback = document.createElement("div");
                feedback.className = "yoru-menu-search-feedback small text-body-secondary mt-2";
                feedback.setAttribute("data-menu-search-feedback", "");
                prompt.before(feedback);
                prompt.className = "mb-0";
                feedback.append(prompt);
            }
            const searchHint = feedback.dataset.searchHint || "Enter a dish name to search.";
            feedback.dataset.searchHint = searchHint;
            search.required = true;
            const descriptionIds = new Set((search.getAttribute("aria-describedby") || "").split(/\s+/).filter(Boolean));
            descriptionIds.add(prompt.id);
            search.setAttribute("aria-describedby", [...descriptionIds].join(" "));
            const originalInvalid = search.getAttribute("aria-invalid");
            const clearSearchPrompt = () => {
                prompt.hidden = true;
                prompt.textContent = "";
                if (originalInvalid === null) search.removeAttribute("aria-invalid");
                else search.setAttribute("aria-invalid", originalInvalid);
            };
            const showSearchPrompt = () => {
                prompt.hidden = false;
                prompt.textContent = searchHint;
                search.setAttribute("aria-invalid", "true");
                search.focus({ preventScroll: true });
            };
            on(search, "invalid", event => {
                event.preventDefault(); // Use the inline prompt instead of a browser bubble.
                showSearchPrompt();
            });
            on(search, "input", () => {
                if (search.value.trim()) clearSearchPrompt();
            });
            on(searchForm, "submit", event => {
                if (!search.value.trim()) {
                    event.preventDefault();
                    event.stopImmediatePropagation(); // Stop the document's GET/AJAX handler.
                    showSearchPrompt();
                    return;
                }
                clearSearchPrompt();
                search.value = search.value.trim();
            }, true);
        }
        const picker = createCategoryPicker(form, on);
        // Snapshot the confirmed server-rendered choices, not edits made while
        // the panel is open. Search has its own unchanged hidden filter values.
        const confirmed = [...form.querySelectorAll('select, input[type="radio"]')].map(node => ({
            node, value: node.value, checked: node.checked
        }));
        const restore = () => {
            confirmed.forEach(({ node, value, checked }) => {
                if (node.type === "radio") node.checked = checked;
                else node.value = value;
            });
            picker?.sync(); picker?.close();
        };
        // Mount the dropdown outside the scrolling collection so it overlays
        // the page without expanding the table or being clipped by split panes.
        const home = document.createComment("Menu filter dropdown home");
        let disposed = false;
        panel.before(home);
        panel.classList.add("yoru-menu-filters", "yoru-menu-filter-dropdown");
        document.body.append(panel);
        const positionDropdown = () => {
            if (disposed || panel.hidden) return;
            const viewport = window.visualViewport;
            const rem = Number.parseFloat(window.getComputedStyle(document.documentElement).fontSize) || 16;
            const gap = rem / 2;
            const left = viewport?.offsetLeft || 0, top = viewport?.offsetTop || 0;
            const width = viewport?.width || window.innerWidth, height = viewport?.height || window.innerHeight;
            panel.style.maxInlineSize = Math.max(1, width - 2 * gap) + "px";
            const anchor = toggle.getBoundingClientRect();
            const anchorBottom = Math.max(top + gap, Math.min(anchor.bottom ?? anchor.top + anchor.height, top + height - gap));
            const anchorTop = Math.max(top + gap, Math.min(anchor.top, top + height - gap));
            const below = Math.max(0, top + height - anchorBottom - 2 * gap);
            const above = Math.max(0, anchorTop - top - 2 * gap);
            const useAbove = below < 18 * rem && above > below;
            panel.style.maxBlockSize = Math.max(1, Math.min(height - 2 * gap, useAbove ? above : below)) + "px";
            const box = panel.getBoundingClientRect();
            const panelWidth = box.width || Math.min(36 * rem, width - 2 * gap);
            panel.style.left = Math.max(left + gap, Math.min((anchor.right ?? anchor.left + anchor.width) - panelWidth,
                left + width - gap - panelWidth)) + "px";
            const desiredTop = useAbove ? anchorTop - gap - box.height : anchorBottom + gap;
            panel.style.top = Math.max(top + gap, Math.min(desiredTop, top + height - gap - box.height)) + "px";
        };
        const setOpen = (open, focus = true) => {
            restore();
            panel.hidden = !open;
            toggle.setAttribute("aria-expanded", String(open));
            if (open) positionDropdown();
            if (focus) (open ? picker?.trigger || form.querySelector('select, input[type="radio"]:checked') || form.querySelector('input[type="radio"]') : toggle)?.focus({ preventScroll: true });
        };
        toggle.hidden = false;
        panel.querySelectorAll("[data-menu-filter-close], [data-menu-filter-cancel]").forEach(button => {
            button.hidden = false;
            on(button, "click", () => setOpen(false));
        });
        setOpen(false, false);
        on(toggle, "click", () => setOpen(panel.hidden));
        on(document, "click", event => {
            if (!panel.hidden && !panel.contains(event.target) && !toggle.contains(event.target)) setOpen(false, false);
        });
        on(window, "resize", positionDropdown);
        on(window, "scroll", event => {
            if (!(event.target instanceof window.Node) || !panel.contains(event.target)) positionDropdown();
        }, true);
        on(window.visualViewport, "resize", positionDropdown);
        on(window.visualViewport, "scroll", positionDropdown);
        const observer = typeof window.ResizeObserver === "function" ? new window.ResizeObserver(positionDropdown) : null;
        observer?.observe(panel);
        on(panel, "keydown", event => {
            if (event.key === "Escape") {
                event.preventDefault(); event.stopPropagation(); setOpen(false);
            }
        });
        on(form, "submit", () => {
            // Apply includes a newly typed search as well as all staged choices.
            // The existing GET/AJAX handler sends exactly one request.
            const query = search ? [...form.elements].find(field => field.name === search.name) : null;
            if (query && search) query.value = search.value;
        }, true);
        disposeFilters = () => {
            if (disposed) return;
            disposed = true;
            controller.abort(); observer?.disconnect(); picker?.dispose();
            panel.classList.remove("yoru-menu-filters", "yoru-menu-filter-dropdown");
            ["left", "top", "max-block-size", "max-inline-size"].forEach(property => panel.style.removeProperty(property));
            panel.hidden = !home.isConnected;
            if (home.parentNode) home.replaceWith(panel); else panel.remove();
            activeFilters = null;
        };
    };
    const initialize = () => {
        initializeBackgrounds();
        initializeFilters();
        initializeThumbnails();
        const form = document.querySelector("[data-menu-item-form]");
        // The drawer's actual form/File node survives list updates. Keep its
        // listeners and Blob preview alive until that form itself is replaced.
        if (form && form === activeForm) { editorCategoryPicker?.sync(); editorCategoryPicker?.close(); return; }
        dispose();
        activeForm = form;
        if (!form) { dispose = () => { }; return; }
        const controller = new AbortController();
        const listen = (node, type, callback, capture = false) => node?.addEventListener(type, callback,
            { signal: controller.signal, capture });
        editorCategoryPicker = createCategoryPicker(form, listen, "ItemInput.CategoryId", "menu-item-category");
        const file = form.querySelector("[data-menu-image]");
        const image = form.querySelector("[data-menu-image-preview]");
        const empty = form.querySelector("[data-menu-image-empty]");
        const error = form.querySelector("[data-menu-image-error]");
        const remove = form.querySelector("[data-menu-remove-image]");
        let objectUrl = null;
        const revoke = () => {
            if (objectUrl) { URL.revokeObjectURL(objectUrl); objectUrl = null; }
        };
        const show = source => {
            if (!image || !empty) return;
            const block = form.querySelector("[data-menu-preview-block]");
            if (block) block.hidden = !source;
            const state = form.querySelector("[data-menu-photo-state]");
            if (state) {
                const unsaved = !!source && source !== image.dataset.currentImage;
                state.hidden = !unsaved;
                state.textContent = unsaved ? "Selected photo · Not saved yet" : "";
            }
            image.hidden = !source;
            const zoom = image.closest("[data-menu-photo-zoom]");
            if (zoom) {
                zoom.hidden = !source;
                if (source) zoom.setAttribute("href", source);
                else zoom.removeAttribute("href");
            }
            if (source) image.src = source;
            else image.removeAttribute("src");
            empty.classList.toggle("d-none", !!source);
            empty.classList.toggle("d-flex", !source);
        };
        const current = () => remove?.checked ? "" : image?.dataset.currentImage || "";
        const validate = () => {
            const selected = file?.files?.[0];
            if (!selected) { file?.setCustomValidity(""); return ""; }
            if (selected.size > 5 * 1024 * 1024) return "Choose an image no larger than 5 MB.";
            if (selected.size === 0 || !/\.(jpe?g|png|webp)$/i.test(selected.name) ||
                !["image/jpeg", "image/png", "image/webp"].includes(selected.type.toLowerCase()))
                return "Choose a JPG, PNG or WebP image.";
            return "";
        };
        const showSelection = ({ initial = false } = {}) => {
            revoke();
            const message = validate();
            file?.setCustomValidity(message);
            if (error && (!initial || message)) error.textContent = message;
            if (message || !file?.files?.length) { show(current()); return; }
            if (remove) remove.checked = false;
            if (typeof URL.createObjectURL === "function") {
                objectUrl = URL.createObjectURL(file.files[0]);
                show(objectUrl);
            }
        };
        listen(file, "change", () => { closePhotoView({ immediate: true, focus: false }); showSelection(); });
        listen(remove, "change", () => {
            closePhotoView({ immediate: true, focus: false });
            if (remove.checked) {
                revoke(); file.value = ""; file.setCustomValidity("");
                if (error) error.textContent = "";
            }
            show(current());
        });
        listen(image, "error", () => {
            show("");
            if (empty) empty.querySelector("p").textContent = "This image could not be displayed. Choose another photo.";
            if (error) error.textContent = "This image could not be displayed. Choose another photo.";
        });
        // Capture phase runs before the workspace fetch handler. Native fallback
        // forms receive the same local checks; server validation is authoritative.
        listen(form, "submit", event => {
            const message = validate();
            file?.setCustomValidity(message);
            if (message) {
                event.preventDefault();
                if (error) error.textContent = message;
                file?.reportValidity();
            }
        }, true);
        const updateName = () => {
            const name = form.querySelector("[data-category-name]");
            const preview = form.querySelector("[data-category-preview-name]");
            const count = form.querySelector("[data-category-name-count]");
            if (preview) preview.textContent = name?.value.trim() || "Your dish name";
            const zoom = image?.closest("[data-menu-photo-zoom]");
            if (zoom) {
                zoom.dataset.photoName = name?.value.trim() || "Your dish name";
                zoom.setAttribute("aria-label", "Enlarge photo of " + zoom.dataset.photoName);
            }
            if (count) count.textContent = String(name?.value.length || 0);
        };
        listen(form.querySelector("[data-category-name]"), "input", updateName);
        listen(form, "yoru:item-form-reset", () => {
            editorCategoryPicker?.close(); editorCategoryPicker?.sync();
            closePhotoView({ immediate: true, focus: false });
            revoke();
            if (file) { file.value = ""; file.setCustomValidity(""); }
            if (error) error.textContent = "";
            show(current()); updateName();
        });
        showSelection({ initial: true });
        updateName();
        dispose = () => { controller.abort(); editorCategoryPicker?.dispose(); editorCategoryPicker = null; revoke(); activeForm = null; };

    };
    document.addEventListener("yoru:workspace-updated", () => {
        closePhotoView({ immediate: true, focus: false }); initialize();
    });
    if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", initialize, { once: true });
    else initialize();
    window.addEventListener("pagehide", () => {
        clearBackgroundMotion();
        closePhotoView({ immediate: true, focus: false }); dispose(); disposeThumbnails(); disposeFilters();
    });
    window.addEventListener("pageshow", event => { if (event.persisted) initialize(); });
})();
