// Owner: Member 3. Progressive enhancements for category management.
// Native Razor forms and links remain the fallback when JavaScript is unavailable.
(() => {
    "use strict";

    // Notifications live outside the replaceable workspace, so they neither
    // shift the cards nor disappear when the next menu response arrives.
    const notifications = new Set();
    const showToast = (message, level = "success") => {
        if (!message) return;
        let container = document.getElementById("category-toast-container");
        if (!container) {
            container = document.createElement("div");
            container.id = "category-toast-container";
            container.className = "toast-container position-fixed bottom-0 end-0 p-3 mw-100";
            container.setAttribute("aria-live", "polite");
            container.setAttribute("aria-relevant", "additions text");
            document.body.append(container);
        }
        // Keep the overlay compact after several rapid operations.
        if (notifications.size >= 3) notifications.values().next().value.remove();
        const error = level === "error";
        const toast = document.createElement("div");
        toast.className = "toast show section-dark border-0 rounded-4 shadow-lg pe-auto";
        toast.setAttribute("data-category-toast", level);
        toast.setAttribute("role", error ? "alert" : "status");
        toast.setAttribute("aria-live", error ? "assertive" : "polite");
        toast.setAttribute("aria-atomic", "true");
        // Only constant icon markup is HTML. Server messages always use textContent.
        toast.innerHTML = '<div class="toast-body p-3 d-flex align-items-start gap-3">' +
            '<svg class="' + (error ? 'text-primary' : 'text-success') + ' flex-shrink-0 align-self-center" width="2.5em" height="2.5em" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" aria-hidden="true">' +
            '<circle cx="12" cy="12" r="10" />' +
            (error ? '<path d="M12 6v7m0 3v1" stroke-linecap="round" />'
                : '<path d="m7 12 3 3 7-7" stroke-linecap="round" stroke-linejoin="round" />') +
            '</svg><div class="flex-grow-1"><p class="small text-white opacity-75 text-uppercase mb-1">YORU / Menu</p>' +
            '<p class="text-white text-break mb-0" data-toast-message></p></div>' +
            '<button type="button" class="btn-close btn-close-white flex-shrink-0" aria-label="Dismiss notification"></button></div>';
        toast.querySelector("[data-toast-message]").textContent = message;
        const previousFocus = document.activeElement;
        const previousFocusId = previousFocus?.id;
        const controller = new window.AbortController();
        const on = (target, type, handler) =>
            target.addEventListener(type, handler, { signal: controller.signal });
        const preference = window.matchMedia("(prefers-reduced-motion: reduce)");
        let timer = null;
        let startedAt = 0;
        let remaining = 5000;
        let hovered = false;
        let focused = false;
        let closing = false;
        let removed = false;
        let animation = null;
        let control;
        const pause = () => {
            if (timer === null) return;
            remaining = Math.max(0, remaining - (window.performance.now() - startedAt));
            window.clearTimeout(timer);
            timer = null;
        };
        const remove = () => {
            if (removed) return;
            removed = true;
            const restoreFocus = toast.contains(document.activeElement);
            pause();
            controller.abort();
            if (animation) animation.cancel();
            toast.remove();
            notifications.delete(control);
            if (restoreFocus) {
                const target = previousFocus?.isConnected ? previousFocus
                    : previousFocusId ? document.getElementById(previousFocusId) : null;
                target?.focus({ preventScroll: true });
            }
        };
        const dismiss = () => {
            if (closing || removed) return;
            closing = true;
            pause();
            if (animation) animation.cancel();
            if (preference.matches || typeof toast.animate !== "function") { remove(); return; }
            animation = toast.animate([
                { opacity: 1, transform: "none" },
                { opacity: 0, transform: "translateY(0.5rem)" }
            ], { duration: 160, easing: "ease-in", fill: "forwards" });
            animation.onfinish = remove;
            animation.oncancel = remove;
        };
        const resume = () => {
            if (error || removed || closing || hovered || focused || document.hidden || timer !== null) return;
            startedAt = window.performance.now();
            timer = window.setTimeout(dismiss, remaining);
        };
        control = { remove };
        notifications.add(control);
        on(toast, "pointerenter", event => {
            if (event.pointerType === "touch") return;
            hovered = true;
            pause();
        });
        on(toast, "pointerleave", () => { hovered = false; resume(); });
        on(toast, "pointercancel", () => { hovered = false; resume(); });
        on(toast, "focusin", () => { focused = true; pause(); });
        on(toast, "focusout", event => {
            if (event.relatedTarget && toast.contains(event.relatedTarget)) return;
            focused = false;
            resume();
        });
        on(toast, "keydown", event => {
            if (event.key === "Escape") { event.stopPropagation(); dismiss(); }
        });
        on(toast.querySelector("button"), "click", dismiss);
        on(document, "visibilitychange", () => { if (document.hidden) pause(); else resume(); });
        if (typeof preference.addEventListener === "function") {
            on(preference, "change", () => {
                if (!preference.matches || !animation) return;
                animation.cancel();
                if (closing) remove();
            });
        }
        container.append(toast);
        if (!preference.matches && typeof toast.animate === "function") {
            animation = toast.animate([
                { opacity: 0, transform: "translateY(0.75rem)" },
                { opacity: 1, transform: "none" }
            ], { duration: 180, easing: "ease-out" });
        }
        resume();
    };
    const takeNotices = root => [...root.querySelectorAll("[data-category-notice]")].map(notice => {
        const result = {
            message: (notice.querySelector("[data-category-message]") || notice).textContent.trim(),
            level: notice.getAttribute("data-category-notice") === "error" ? "error" : "success"
        };
        notice.remove();
        return result;
    });
    const showNotices = notices => notices.forEach(({ message, level }) => showToast(message, level));
    const initialNotices = takeNotices(document);

    // Dispose listeners, timers and animations before binding a fresh workspace.
    const initializeEffects = ({ guide = false, focusName = false } = {}) => {
        const controller = new window.AbortController();
        const timers = new Set();
        const animations = new Set();
        const on = (target, type, handler) =>
            target.addEventListener(type, handler, { signal: controller.signal });
        const animate = (element, frames, options) => {
            const animation = element.animate(frames, options);
            animations.add(animation);
            return animation;
        };
        const cancelLater = id => { window.clearTimeout(id); timers.delete(id); };
        const later = (fn, delay) => {
            const id = window.setTimeout(() => { timers.delete(id); fn(); }, delay);
            timers.add(id);
            return id;
        };
        const cleanup = () => {
            controller.abort();
            timers.forEach(id => window.clearTimeout(id));
            animations.forEach(animation => animation.cancel());
        };
        // Hero and shortcuts have their own restrained motion, separate from
        // summary-card lifts. All transforms are skipped for reduced motion.
        const attachHoverMotion = (trigger, layers) => {
            if (!trigger || layers.length === 0) return;
            const preference = window.matchMedia("(prefers-reduced-motion: reduce)");
            let hovered = trigger.matches(":hover");
            let focused = trigger.contains(document.activeElement);
            let wasActive = false;
            const running = new Map();
            const update = () => {
                const active = hovered || focused;
                if (active === wasActive) return;
                wasActive = active;
                layers.forEach(({ element, transform }) => {
                    const previous = window.getComputedStyle(element).transform;
                    const existing = running.get(element);
                    if (existing) existing.cancel();
                    running.delete(element);
                    if (preference.matches || typeof element.animate !== "function") return;
                    const animation = animate(element, [
                        { transform: previous }, { transform: active ? transform : "none" }
                    ], { duration: 220, easing: "ease-out", fill: "forwards" });
                    running.set(element, animation);
                    if (!active) animation.onfinish = () => {
                        if (running.get(element) === animation) {
                            animation.cancel();
                            running.delete(element);
                        }
                    };
                });
            };
            on(trigger, "pointerenter", event => {
                if (event.pointerType === "touch") return;
                hovered = true;
                update();
            });
            const leave = () => { hovered = false; update(); };
            on(trigger, "pointerleave", leave);
            on(trigger, "pointercancel", leave);
            on(trigger, "focusin", () => { focused = true; update(); });
            on(trigger, "focusout", event => {
                if (event.relatedTarget && trigger.contains(event.relatedTarget)) return;
                focused = false;
                update();
            });
            if (typeof preference.addEventListener === "function") {
                on(preference, "change", () => {
                    if (!preference.matches) return;
                    running.forEach(animation => animation.cancel());
                    running.clear();
                });
            }
            update();
        };

        document.querySelectorAll("[data-category-hero]").forEach(hero => {
            const backdrop = hero.querySelector("[data-hero-backdrop]");
            if (backdrop) attachHoverMotion(hero, [{ element: backdrop, transform: "scale(1.025)" }]);
        });
        document.querySelectorAll("[data-category-shortcut]").forEach(shortcut => {
            const layers = [{ element: shortcut, transform: "translateY(-0.125rem)" }];
            const plus = shortcut.querySelector("[data-shortcut-plus]");
            if (plus) layers.push({ element: plus, transform: "translateX(0.125rem)" });
            attachHoverMotion(shortcut, layers);
        });

        // Listen on each stable grid column so lifting its card cannot jitter
        // the hover boundary. These statistics remain non-clickable content.
        document.querySelectorAll("[data-category-summary]").forEach(wrapper => {
            const surface = wrapper.querySelector("[data-summary-surface]");
            if (!surface) return;
            const artwork = surface.querySelector("[data-summary-art]");
            const originalOpacity = artwork ? artwork.getAttribute("opacity") || "0.1" : "0.1";
            const preference = window.matchMedia("(prefers-reduced-motion: reduce)");
            let surfaceAnimation = null;
            let artworkAnimation = null;

            const setHovered = active => {
                const current = window.getComputedStyle(surface);
                const fromTransform = current.transform;
                const fromShadow = current.boxShadow;
                const fromOpacity = artwork ? window.getComputedStyle(artwork).opacity : "1";
                if (surfaceAnimation) surfaceAnimation.cancel();
                if (artworkAnimation) artworkAnimation.cancel();
                surfaceAnimation = null;
                artworkAnimation = null;

                surface.classList.toggle("shadow-sm", !active);
                surface.classList.toggle("shadow-lg", active);
                const targetOpacity = active ? String(Number(originalOpacity) + 0.06) : originalOpacity;
                if (artwork) artwork.setAttribute("opacity", targetOpacity);

                if (preference.matches || typeof surface.animate !== "function") return;
                const animation = animate(surface, [
                    { transform: fromTransform, boxShadow: fromShadow },
                    { transform: active ? "translateY(-0.25rem)" : "none",
                        boxShadow: window.getComputedStyle(surface).boxShadow }
                ], { duration: 180, easing: "ease-out", fill: "forwards" });
                surfaceAnimation = animation;
                if (!active) animation.onfinish = () => {
                    if (surfaceAnimation === animation) {
                        animation.cancel();
                        surfaceAnimation = null;
                    }
                };
                if (artwork && typeof artwork.animate === "function") {
                    artworkAnimation = animate(artwork, 
                        [{ opacity: fromOpacity }, { opacity: targetOpacity }],
                        { duration: 180, easing: "ease-out" });
                }
            };

            on(wrapper, "pointerenter", event => {
                if (event.pointerType !== "touch") setHovered(true);
            });
            on(wrapper, "pointerleave", () => setHovered(false));
            on(wrapper, "pointercancel", () => setHovered(false));
            if (wrapper.matches(":hover")) setHovered(true);
        });

        document.querySelectorAll("[data-category-collection]").forEach(card => {
            const preference = window.matchMedia("(prefers-reduced-motion: reduce)");
            let hovered = card.matches(":hover");
            let focused = card.contains(document.activeElement);
            let animation = null;
            const updateShadow = () => {
                const active = hovered || focused;
                if (card.classList.contains("shadow") === active) return;
                const previous = window.getComputedStyle(card).boxShadow;
                if (animation) animation.cancel();
                animation = null;
                card.classList.toggle("shadow-sm", !active);
                card.classList.toggle("shadow", active);
                const next = window.getComputedStyle(card).boxShadow;
                if (!preference.matches && typeof card.animate === "function" && previous !== next) {
                    animation = animate(card, [{ boxShadow: previous }, { boxShadow: next }],
                        { duration: 180, easing: "ease-out" });
                }
            };
            on(card, "pointerenter", event => {
                if (event.pointerType === "touch") return;
                hovered = true;
                updateShadow();
            });
            const leave = () => { hovered = false; updateShadow(); };
            on(card, "pointerleave", leave);
            on(card, "pointercancel", leave);
            on(card, "focusin", () => { focused = true; updateShadow(); });
            on(card, "focusout", event => {
                if (event.relatedTarget && card.contains(event.relatedTarget)) return;
                focused = false;
                updateShadow();
            });
            updateShadow();
        });

        document.querySelectorAll("[data-category-row]").forEach(row => {
            const order = row.querySelector("[data-category-order]");
            const name = row.querySelector("[data-category-row-name]");
            const accent = row.querySelector("[data-category-row-accent]");
            if (!order || !name || !accent) return;
            const editing = row.getAttribute("data-editing") === "true";
            let hovered = false;
            let focused = row.contains(document.activeElement);
            const updateRow = () => {
                const active = editing || hovered || focused;
                row.classList.toggle("table-active", active);
                order.classList.toggle("bg-primary", active);
                order.classList.toggle("text-white", active);
                order.classList.toggle("border-primary", active);
                name.classList.toggle("text-primary", active);
                accent.classList.toggle("opacity-100", active);
                accent.classList.toggle("opacity-0", !active);
            };
            on(row, "pointerenter", event => {
                if (event.pointerType === "touch") return;
                hovered = true;
                updateRow();
            });
            const leave = () => { hovered = false; updateRow(); };
            on(row, "pointerleave", leave);
            on(row, "pointercancel", leave);
            on(row, "focusin", () => { focused = true; updateRow(); });
            on(row, "focusout", event => {
                if (event.relatedTarget && row.contains(event.relatedTarget)) return;
                focused = false;
                updateRow();
            });
            updateRow();
        });

        const form = document.querySelector("[data-category-editor]");
        if (!form) return cleanup;

        const nameInput = form.querySelector("[data-category-name]");
        const namePreview = form.querySelector("[data-category-preview-name]");
        const nameCount = form.querySelector("[data-category-name-count]");
        if (!nameInput || !namePreview || !nameCount) return cleanup;

        const updatePreview = () => {
            namePreview.textContent = nameInput.value.trim() || "Your category name";
            nameCount.textContent = String(nameInput.value.length);
        };

        on(nameInput, "input", updatePreview);
        updatePreview();

        const panel = document.getElementById("category-editor");
        if (!panel) return cleanup;

        const idInput = form.querySelector('input[name="Input.Id"]');
        const motionPreference = window.matchMedia("(prefers-reduced-motion: reduce)");
        let hovered = panel.matches(":hover");
        let focused = panel.contains(document.activeElement);
        let shortcutActive = false;
        let highlightTimer = null;
        let shadowAnimation = null;

        const updateCardShadow = () => {
            const active = hovered || focused || shortcutActive;
            const strong = shortcutActive;
            if (panel.classList.contains("shadow-lg") === strong &&
                panel.classList.contains("shadow") === (active && !strong)) return;

            // Animate between the theme's Bootstrap shadows, with no custom colours,
            // border changes or movement. Classes also work without animation support.
            const previousShadow = window.getComputedStyle(panel).boxShadow;
            if (shadowAnimation) {
                shadowAnimation.cancel();
                shadowAnimation = null;
            }
            panel.classList.toggle("shadow-sm", !active);
            panel.classList.toggle("shadow", active && !strong);
            panel.classList.toggle("shadow-lg", strong);
            const nextShadow = window.getComputedStyle(panel).boxShadow;

            if (!motionPreference.matches && typeof panel.animate === "function" &&
                previousShadow !== nextShadow) {
                shadowAnimation = animate(panel, 
                    [{ boxShadow: previousShadow }, { boxShadow: nextShadow }],
                    { duration: 180, easing: "ease-out" }
                );
            }
        };

        on(panel, "pointerenter", event => {
            if (event.pointerType === "touch") return;
            hovered = true;
            updateCardShadow();
        });
        on(panel, "pointerleave", () => {
            hovered = false;
            updateCardShadow();
        });
        on(panel, "pointercancel", () => {
            hovered = false;
            updateCardShadow();
        });
        on(panel, "focusin", () => {
            focused = true;
            updateCardShadow();
        });
        on(panel, "focusout", event => {
            if (event.relatedTarget && panel.contains(event.relatedTarget)) return;
            focused = false;
            updateCardShadow();
        });
        updateCardShadow();

        const revealEditor = () => {
            if (highlightTimer !== null) cancelLater(highlightTimer);
            shortcutActive = true;
            updateCardShadow();
            nameInput.focus({ preventScroll: true });
            panel.scrollIntoView({
                behavior: motionPreference.matches ? "auto" : "smooth",
                block: "center",
                inline: "nearest"
            });
            highlightTimer = later(() => {
                highlightTimer = null;
                shortcutActive = false;
                // Keep the hover appearance while the pointer or keyboard is inside.
                updateCardShadow();
            }, 2200);
        };


        on(panel, "yoru:new-category", revealEditor);
        if (guide) window.requestAnimationFrame(revealEditor);
        else if (focusName) nameInput.focus({ preventScroll: true });
        return cleanup;
    };

    const modulePath = window.location.pathname.replace(/\/+$/, "");
    const sameModule = url => url.origin === window.location.origin &&
        url.pathname.replace(/\/+$/, "") === modulePath;
    const workspace = () => document.querySelector("[data-category-workspace]");
    const historyKey = "yoruCategoryView";
    const route = () => window.location.pathname + window.location.search;
    let disposeEffects = initializeEffects({ guide: window.location.hash === "#category-editor" });
    showNotices(initialNotices);
    const enhanced = !!workspace() && typeof window.fetch === "function" &&
        typeof window.DOMParser === "function" && typeof window.FormData === "function";
    let busy = false;
    let queuedPop = null;

    // Remove the old full-reload scroll record when upgrading from that version.
    try { window.sessionStorage.removeItem("yoru.menuCategories.pendingScroll"); } catch { }

    const captureViewport = element => {
        const anchor = element?.closest?.("#category-editor, #category-collection");
        return {
            route: route(), y: window.scrollY, anchor: anchor ? anchor.id : null,
            offset: anchor ? -anchor.getBoundingClientRect().top : 0
        };
    };
    const validViewport = view => view && Number.isFinite(view.y) &&
        Number.isFinite(view.offset) && view.route === route();
    const restoreViewport = view => {
        if (!view || !Number.isFinite(view.y) || !Number.isFinite(view.offset)) return;
        const anchor = view.anchor === "category-collection" || view.anchor === "category-editor"
            ? document.getElementById(view.anchor) : null;
        const top = anchor
            ? window.scrollY + anchor.getBoundingClientRect().top + view.offset : view.y;
        window.scrollTo({ top: Math.max(0, top), behavior: "instant" });
    };
    const saveHistoryViewport = view => {
        window.history.replaceState({ ...(window.history.state || {}),
            [historyKey]: { ...view, route: route() } }, "", window.location.href);
    };
    const ordinaryClick = (event, link) => !event.defaultPrevented && event.button === 0 &&
        !event.ctrlKey && !event.metaKey && !event.shiftKey && !event.altKey &&
        !link.hasAttribute("download") && (!link.target || link.target === "_self");
    const requestError = message => showToast(message, "error");

    const requestPage = async (destination, {
        method = "GET", body = null, mode = "push", view = null,
        guide = false, focusName = false, trigger = null
    } = {}) => {
        if (busy) return;
        busy = true;
        const oldWorkspace = workspace();
        const position = view || captureViewport(trigger || document.activeElement);
        const focusedId = document.activeElement?.id;
        const fields = [...oldWorkspace.querySelectorAll('input:not([type="hidden"]), select, textarea')]
            .map(element => ({ element, readOnly: element.readOnly, inert: !!element.inert }));
        if (mode !== "pop") saveHistoryViewport(position);
        oldWorkspace.setAttribute("aria-busy", "true");
        const status = oldWorkspace.querySelector("[data-category-live-status]");
        if (status) status.textContent = "Updating menu categories.";
        // Bootstrap gives disabled controls a blue-grey background. Lock editing
        // without toggling that appearance; the busy guard blocks repeat actions.
        fields.forEach(({ element }) => {
            if (element.tagName === "SELECT") element.inert = true;
            else element.readOnly = true;
        });
        try {
            const response = await window.fetch(destination.href, {
                method, body, credentials: "same-origin", redirect: "follow",
                cache: "no-store", headers: { "Accept": "text/html" }
            });
            if (!response.ok) throw new Error("request-failed");
            const returnedUrl = new URL(response.url || destination.href, window.location.href);
            if (!sameModule(returnedUrl)) {
                // An expired login may return the normal sign-in redirect.
                if (returnedUrl.origin !== window.location.origin) throw new Error("unexpected-redirect");
                window.location.assign(returnedUrl.href);
                return;
            }
            const html = await response.text();
            const resultDocument = new window.DOMParser().parseFromString(html, "text/html");
            const candidates = resultDocument.querySelectorAll("[data-category-workspace]");
            if (candidates.length !== 1) throw new Error("unexpected-response");
            const notices = takeNotices(candidates[0]);
            // Browser Back/Forward may have been requested while a write was in flight.
            // Finish that write, then render the queued GET without replaying the POST.
            if (queuedPop) { showNotices(notices); return; }
            const replacement = document.importNode(candidates[0], true);
            replacement.querySelectorAll("script").forEach(script => script.remove());
            disposeEffects();
            oldWorkspace.replaceWith(replacement);
            if (method === "GET") returnedUrl.hash = destination.hash;
            if (mode === "push") {
                window.history.pushState({}, "", returnedUrl);
            } else if (mode !== "pop" && response.redirected) {
                window.history.replaceState(window.history.state, "", returnedUrl);
            }
            // New HTML contains fresh antiforgery fields. Its scripts are not executed.
            // Rebind only our effects and, when present, the shared validation plugin.
            const unobtrusive = window.jQuery?.validator?.unobtrusive;
            if (unobtrusive && typeof unobtrusive.parse === "function") unobtrusive.parse(replacement);
            restoreViewport(position);
            disposeEffects = initializeEffects({ guide, focusName });
            if (!guide && !focusName) {
                let target = focusedId ? document.getElementById(focusedId) : null;
                if (!target || !replacement.contains(target)) {
                    if (trigger?.closest('nav[aria-label="Category pages"]')) {
                        target = replacement.querySelector('[aria-current="page"]');
                    } else if (trigger?.tagName === "FORM" && method === "GET") {
                        target = replacement.querySelector('form[method="get"] button[type="submit"]');
                    } else if (trigger?.tagName === "A") {
                        target = [...replacement.querySelectorAll("a[data-category-keep-scroll]")]
                            .find(link => link.href === destination.href);
                    } else if (method === "POST") {
                        target = replacement.querySelector("#collection-title");
                    }
                }
                if (target && replacement.contains(target)) {
                    if (!target.matches("a[href], button, input, select, textarea, [tabindex]")) {
                        target.setAttribute("tabindex", "-1");
                    }
                    target.focus({ preventScroll: true });
                }
            }
            const newStatus = replacement.querySelector("[data-category-live-status]");
            if (newStatus) newStatus.textContent = notices.length ? "" : "Menu categories updated.";
            showNotices(notices);
            saveHistoryViewport(captureViewport(
                position.anchor ? document.getElementById(position.anchor) : null));
        } catch {
            requestError(method === "POST"
                ? "The result could not be loaded. Refresh the page to check whether the change was saved before submitting again."
                : "The menu could not be updated. Please try again or refresh the page.");
        } finally {
            fields.forEach(({ element, readOnly, inert }) => {
                if (!element.isConnected) return;
                if (element.tagName === "SELECT") element.inert = inert;
                else element.readOnly = readOnly;
            });
            if (oldWorkspace.isConnected) {
                oldWorkspace.removeAttribute("aria-busy");
                if (status) status.textContent = "";
            }
            busy = false;
            if (queuedPop) {
                const pending = queuedPop;
                queuedPop = null;
                void requestPage(pending.url, { mode: "pop", view: pending.view });
            }
        }
    };

    document.addEventListener("click", event => {
        const link = event.target.closest?.("a[data-category-jump], a[data-category-keep-scroll]");
        if (!link || !ordinaryClick(event, link)) return;
        const destination = new URL(link.href, window.location.href);
        if (!sameModule(destination)) return;
        const isNew = link.hasAttribute("data-category-jump");
        // The hero stays mounted while the workspace changes. Build its New
        // destination from the current view so filters and ShowAll stay current.
        if (isNew) {
            destination.search = window.location.search;
            destination.searchParams.delete("editId");
            destination.hash = "category-editor";
        }
        const idInput = document.querySelector('[data-category-editor] input[name="Input.Id"]');
        if (busy) { event.preventDefault(); return; }
        if (isNew && idInput && !idInput.value) {
            event.preventDefault();
            const current = new URL(window.location.href);
            current.hash = "category-editor";
            window.history.replaceState(window.history.state, "", current);
            document.getElementById("category-editor")?.dispatchEvent(
                new window.CustomEvent("yoru:new-category"));
            return;
        }
        if (!enhanced) return;
        event.preventDefault();
        void requestPage(destination, {
            guide: isNew, focusName: !isNew && destination.searchParams.has("editId") &&
                destination.hash === "#category-editor", trigger: link
        });
    });

    document.addEventListener("submit", event => {
        const form = event.target;
        if (!enhanced || event.defaultPrevented ||
            !form.matches?.("form[data-category-keep-scroll]") ||
            (form.target && form.target !== "_self")) return;
        const submitter = event.submitter;
        const method = (submitter?.getAttribute("formmethod") || form.method || "get").toUpperCase();
        const destination = new URL(submitter?.getAttribute("formaction") || form.action, window.location.href);
        if (!sameModule(destination) || !["GET", "POST"].includes(method)) return;
        event.preventDefault();
        if (busy) return;
        // Construct FormData before locking editing so the antiforgery token and
        // all successful form controls are included in the original request.
        const data = new window.FormData(form);
        if (submitter?.name) data.append(submitter.name, submitter.value);
        if (method === "GET") {
            destination.search = "";
            for (const [name, value] of data) if (typeof value === "string") {
                destination.searchParams.append(name, value);
            }
        }
        void requestPage(destination, {
            method, body: method === "POST" ? data : null,
            mode: method === "POST" ? "replace" : "push",
            focusName: form.hasAttribute("data-category-editor"), trigger: form
        });
    });

    if (enhanced) {
        const previousRestoration = window.history.scrollRestoration;
        window.history.scrollRestoration = "manual";
        window.addEventListener("popstate", event => {
            const view = validViewport(event.state?.[historyKey]) ? event.state[historyKey]
                : { route: route(), y: 0, anchor: null, offset: 0 };
            const destination = new URL(window.location.href);
            if (!sameModule(destination)) return;
            if (busy) queuedPop = { url: destination, view };
            else void requestPage(destination, { mode: "pop", view });
        });
        window.addEventListener("pagehide", () => {
            saveHistoryViewport(captureViewport(document.activeElement));
            window.history.scrollRestoration = previousRestoration;
        });
        window.addEventListener("pageshow", event => {
            window.history.scrollRestoration = "manual";
            const view = window.history.state?.[historyKey];
            if (validViewport(view)) restoreViewport(view);
        });
    }
})();
