// Owner: Member 3. Progressive enhancements for category management.
// Native Razor forms and links remain the fallback when JavaScript is unavailable.
(() => {
    "use strict";

    // The shared layout owns first-load notifications. AJAX responses include
    // that same markup outside the workspace: reuse it with the shared CSS and
    // bind its controls here, since scripts in a parsed response must not run.
    let currentNotification = null;
    const showToast = (message, level = "success", returnedToast = null) => {
        if (!message) return;
        const kind = ["success", "error", "info"].includes(level) ? level : "info";
        currentNotification?.remove();
        // Match the shared layout's single-notification presentation.
        document.querySelectorAll(".yoru-toast-wrap").forEach(wrapper => wrapper.remove());
        const wrapper = document.createElement("div");
        wrapper.className = "yoru-toast-wrap";
        const toast = returnedToast ? document.importNode(returnedToast, true) : document.createElement("div");
        if (!returnedToast) {
            const details = {
                success: { title: "All set", japanese: "完了", icon: "bi-check-lg" },
                error: { title: "Oops", japanese: "注意", icon: "bi-exclamation-lg" },
                info: { title: "Notice", japanese: "お知らせ", icon: "bi-info-lg" }
            }[kind];
            toast.className = "yoru-toast yoru-toast--" + kind;
            toast.setAttribute("data-toast", "");
            toast.innerHTML = '<span class="yoru-toast-seal" aria-hidden="true"><i class="bi ' + details.icon + '"></i></span>' +
                '<div class="yoru-toast-body"><p class="yoru-toast-title"><span class="yoru-toast-jp"></span></p>' +
                '<p class="yoru-toast-msg"></p></div>' +
                '<button type="button" class="yoru-toast-close" aria-label="Close notification"><i class="bi bi-x-lg"></i></button>' +
                '<span class="yoru-toast-bar" aria-hidden="true"></span>';
            toast.querySelector(".yoru-toast-jp").textContent = details.japanese;
            toast.querySelector(".yoru-toast-title").append(document.createTextNode(details.title));
        }
        toast.querySelectorAll("script").forEach(script => script.remove());
        toast.classList.remove("is-leaving");
        toast.setAttribute("role", kind === "error" ? "alert" : "status");
        toast.setAttribute("aria-live", "polite");
        toast.setAttribute("aria-atomic", "true");
        toast.querySelector(".yoru-toast-msg").textContent = message;
        const previousFocus = document.activeElement, previousFocusId = previousFocus?.id;
        const controller = new window.AbortController();
        const on = (node, type, handler) => node?.addEventListener(type, handler, { signal: controller.signal });
        let timer = null, removed = false, control;
        const remove = () => {
            if (removed) return;
            removed = true;
            const restoreFocus = toast.contains(document.activeElement);
            if (timer !== null) window.clearTimeout(timer);
            controller.abort(); wrapper.remove();
            if (currentNotification === control) currentNotification = null;
            if (restoreFocus) {
                const target = previousFocus?.isConnected ? previousFocus
                    : previousFocusId ? document.getElementById(previousFocusId) : null;
                target?.focus({ preventScroll: true });
            }
        };
        const dismiss = () => {
            if (removed || toast.classList.contains("is-leaving")) return;
            toast.classList.add("is-leaving");
            timer = window.setTimeout(remove, 400);
        };
        // The shared five-second CSS progress animation pauses on hover/focus.
        on(toast.querySelector(".yoru-toast-bar"), "animationend", event => {
            if (event.animationName === "yoru-toast-time") dismiss();
        });
        on(toast.querySelector(".yoru-toast-close"), "click", dismiss);
        on(toast, "keydown", event => {
            if (event.key === "Escape") { event.preventDefault(); event.stopPropagation(); dismiss(); }
        });
        on(window, "pagehide", remove);
        control = { remove }; currentNotification = control;
        wrapper.append(toast); document.body.append(wrapper);
    };
    const takeNotices = root => [...root.querySelectorAll(".yoru-toast-wrap [data-toast]")].map(toast => {
        const result = {
            message: toast.querySelector(".yoru-toast-msg")?.textContent.trim() || "",
            level: toast.classList.contains("yoru-toast--error") ? "error"
                : toast.classList.contains("yoru-toast--success") ? "success" : "info",
            toast
        };
        toast.closest(".yoru-toast-wrap")?.remove();
        return result;
    });
    const showNotices = notices => notices.forEach(({ message, level, toast }) => showToast(message, level, toast));
    let descriptionSequence = 0;
    const menuTabs = () => document.querySelector('[data-menu-hero-controls] nav[aria-label="Menu management sections"]');
    const captureTab = () => {
        const nav = menuTabs(), active = nav?.querySelector('[aria-current="page"]');
        if (!active) return null;
        const box = (nav.querySelector("[data-menu-tab-indicator]") || active).getBoundingClientRect();
        const origin = nav.getBoundingClientRect();
        return { id: active.id, left: box.left - origin.left - nav.clientLeft, width: box.width };
    };
    const updateHeroControls = returned => {
        const current = document.querySelector("[data-menu-hero-controls]");
        if (!current || !returned) return;
        const incoming = document.importNode(returned, true);
        incoming.querySelectorAll("script").forEach(script => script.remove());
        const nav = menuTabs();
        const nextNav = incoming.querySelector('nav[aria-label="Menu management sections"]');
        const links = [...nav?.querySelectorAll("a[id]") || []];
        const nextLinks = [...nextNav?.querySelectorAll("a[id]") || []];
        if (!nav || !nextNav || links.length !== nextLinks.length || !links.length ||
            links.some(link => !nextLinks.some(next => next.id === link.id))) {
            current.replaceWith(incoming);
            return;
        }
        // Keep the nav, labels and sole highlight mounted. Recreating the tabs
        // starts Bootstrap background transitions and briefly paints two pills.
        links.forEach(link => {
            const next = nextLinks.find(candidate => candidate.id === link.id);
            [...link.attributes].forEach(attribute => {
                if (!next.hasAttribute(attribute.name)) link.removeAttribute(attribute.name);
            });
            [...next.attributes].forEach(attribute => link.setAttribute(attribute.name, attribute.value));
        });
        [...current.children].filter(child => child !== nav).forEach(child => child.remove());
        [...incoming.children].filter(child => child !== nextNav).forEach(child => current.append(child));
    };

    // Dispose listeners and animations before binding a fresh workspace.
    const initializeEffects = ({ guide = false, focusName = false, tabFrom = null } = {}) => {
        const controller = new window.AbortController();
        const animations = new Set();
        const disconnectors = [];
        const preference = window.matchMedia("(prefers-reduced-motion: reduce)");
        const on = (target, type, handler) =>
            target.addEventListener(type, handler, { signal: controller.signal });
        const animate = (element, frames, options) => {
            const animation = element.animate(frames, options);
            animations.add(animation);
            animation.oncancel = () => animations.delete(animation);
            if (options.fill !== "forwards") animation.onfinish = () => animations.delete(animation);
            return animation;
        };
        const cancelAnimations = () => {
            animations.forEach(animation => animation.cancel());
            animations.clear();
        };
        const cleanup = () => {
            controller.abort();
            disconnectors.forEach(disconnect => disconnect());
            cancelAnimations();
        };
        if (typeof preference.addEventListener === "function") {
            on(preference, "change", () => { if (preference.matches) cancelAnimations(); });
        }

        // Only a completed navigation supplies tabFrom. Failed/cancelled requests
        // keep the previous selection; labels and the original hero stay still.
        const nav = menuTabs(), selected = nav?.querySelector('[aria-current="page"]');
        if (selected) {
            nav.classList.add("yoru-menu-tabs");
            let indicator = nav.querySelector("[data-menu-tab-indicator]");
            if (!indicator) {
                indicator = document.createElement("span");
                indicator.className = "yoru-menu-tab-indicator";
                indicator.setAttribute("data-menu-tab-indicator", "");
                indicator.setAttribute("aria-hidden", "true");
                nav.prepend(indicator);
            }
            const positionIndicator = () => {
                const box = selected.getBoundingClientRect(), origin = nav.getBoundingClientRect();
                const left = box.left - origin.left - nav.clientLeft;
                indicator.style.left = left + "px";
                indicator.style.top = (box.top - origin.top - nav.clientTop) + "px";
                indicator.style.width = box.width + "px";
                indicator.style.height = box.height + "px";
                return { left, top: box.top - origin.top - nav.clientTop, width: box.width, height: box.height };
            };
            let destination = positionIndicator();
            let slide = null;
            if (tabFrom && tabFrom.id !== selected.id && !preference.matches &&
                typeof indicator.animate === "function") {
                slide = animate(indicator, [
                    {
                        transform: "translateX(" + (tabFrom.left - destination.left) + "px) scaleX(" +
                            (destination.width > 0 ? tabFrom.width / destination.width : 1) + ")"
                    },
                    { transform: "translateX(0) scaleX(1)" }
                ], { duration: 320, easing: "cubic-bezier(0.4, 0, 0.2, 1)" });
            }
            const refresh = () => {
                const next = positionIndicator();
                // ResizeObserver's initial delivery must not cancel the slide.
                if (Object.keys(next).some(key => next[key] !== destination[key])) slide?.cancel();
                destination = next;
            };
            on(window, "resize", refresh);
            if (typeof window.ResizeObserver === "function") {
                const observer = new window.ResizeObserver(refresh);
                observer.observe(nav);
                disconnectors.push(() => observer.disconnect());
            }
        }

        // Native <details> remains the no-JS fallback. Enhanced controls keep
        // one real button after the text, so focus and Show less stay at bottom.
        document.querySelectorAll("details.yoru-menu-description").forEach(details => {
            const preview = details.querySelector(".yoru-description-preview");
            const full = details.querySelector(".yoru-description-text");
            if (!preview || !full) return;
            const copy = document.createElement("div"), button = document.createElement("button");
            copy.className = details.className + " yoru-description-copy";
            preview.classList.remove("d-block");
            full.classList.remove("mt-1");
            full.id = "menu-description-" + (++descriptionSequence);
            button.type = "button";
            button.className = "yoru-description-toggle";
            button.setAttribute("aria-controls", full.id);
            let expanded = details.open;
            const update = () => {
                preview.hidden = expanded;
                full.hidden = !expanded;
                button.setAttribute("aria-expanded", String(expanded));
                button.textContent = expanded ? "Show less" : "Show more";
            };
            update();
            copy.append(preview, full, button);
            details.replaceWith(copy);
            on(button, "click", () => { expanded = !expanded; update(); });
        });

        // The hero stays still. New buttons keep their separate spring response.
        document.querySelectorAll("[data-category-shortcut]").forEach(shortcut => {
            const running = new Map();
            let hovered = shortcut.matches(":hover");
            let focused = shortcut.contains(document.activeElement);
            let pressed = false;
            let lastState = "rest";
            const update = () => {
                const active = hovered || focused;
                const state = pressed ? "pressed" : active ? "active" : "rest";
                if (state === lastState) return;
                lastState = state;
                const previous = window.getComputedStyle(shortcut).transform;
                running.get(shortcut)?.cancel();
                running.delete(shortcut);
                if (preference.matches || typeof shortcut.animate !== "function") return;
                const transform = pressed ? "translateY(0) scale(0.97)"
                    : active ? "translateY(-0.125rem) scale(1.025)" : "none";
                // A small overshoot and settle gives a spring-like response.
                // The plus remains part of the label and never rotates.
                const frames = [{ transform: previous }];
                if (!pressed) frames.push({
                    transform: active ? "translateY(-0.15rem) scale(1.04)"
                        : "translateY(0.025rem) scale(0.995)", offset: 0.65
                });
                frames.push({ transform });
                const animation = animate(shortcut, frames,
                    { duration: pressed ? 90 : 360, easing: "ease-out", fill: "forwards" });
                running.set(shortcut, animation);
                if (state === "rest") animation.onfinish = () => {
                    if (running.get(shortcut) !== animation) return;
                    animation.cancel();
                    running.delete(shortcut);
                };
            };
            on(shortcut, "pointerenter", event => {
                if (event.pointerType === "touch") return;
                hovered = true; update();
            });
            on(shortcut, "pointerleave", () => { hovered = false; pressed = false; update(); });
            on(shortcut, "pointercancel", () => { hovered = false; pressed = false; update(); });
            on(shortcut, "pointerdown", event => {
                if (event.button !== 0) return;
                pressed = true; update();
            });
            on(window, "pointerup", () => { if (pressed) { pressed = false; update(); } });
            on(window, "blur", () => { hovered = false; pressed = false; focused = false; update(); });
            on(shortcut, "keydown", event => {
                if (event.key === "Enter" && !event.repeat) { pressed = true; update(); }
            });
            on(window, "keyup", event => {
                if (event.key === "Enter" && pressed) { pressed = false; update(); }
            });
            on(shortcut, "focusin", () => { focused = true; update(); });
            on(shortcut, "focusout", event => {
                if (event.relatedTarget && shortcut.contains(event.relatedTarget)) return;
                focused = false; pressed = false; update();
            });
            if (typeof preference.addEventListener === "function") {
                on(preference, "change", () => {
                    running.clear();
                    lastState = "";
                    update();
                });
            }
            update();
        });

        // Keep the hover boundary on the stable summary column, not its moving card.
        document.querySelectorAll("[data-category-summary]").forEach(wrapper => {
            const surface = wrapper.querySelector("[data-summary-surface]");
            if (!surface) return;
            const artwork = surface.querySelector("[data-summary-art]");
            const restingOpacity = artwork?.getAttribute("opacity") || "0.1";
            let hovered = false;
            let surfaceAnimation = null;
            let artworkAnimation = null;
            const update = () => {
                const before = window.getComputedStyle(surface);
                const fromTransform = before.transform;
                const fromShadow = before.boxShadow;
                const fromOpacity = artwork ? window.getComputedStyle(artwork).opacity : "1";
                surfaceAnimation?.cancel();
                artworkAnimation?.cancel();
                surfaceAnimation = null;
                artworkAnimation = null;
                surface.classList.toggle("shadow-sm", !hovered);
                surface.classList.toggle("shadow-lg", hovered);
                const opacity = hovered ? String(Math.min(1, Number(restingOpacity) + 0.06)) : restingOpacity;
                artwork?.setAttribute("opacity", opacity);
                if (preference.matches || typeof surface.animate !== "function") return;
                const animation = animate(surface, [
                    { transform: fromTransform, boxShadow: fromShadow },
                    {
                        transform: hovered ? "translateY(-0.25rem)" : "none",
                        boxShadow: window.getComputedStyle(surface).boxShadow
                    }
                ], { duration: 180, easing: "ease-out", fill: "forwards" });
                surfaceAnimation = animation;
                if (!hovered) animation.onfinish = () => {
                    if (surfaceAnimation !== animation) return;
                    animation.cancel();
                    surfaceAnimation = null;
                };
                if (artwork && typeof artwork.animate === "function") {
                    artworkAnimation = animate(artwork, [{ opacity: fromOpacity }, { opacity }],
                        { duration: 180, easing: "ease-out" });
                }
            };
            on(wrapper, "pointerenter", event => {
                if (event.pointerType === "touch" || hovered) return;
                hovered = true; update();
            });
            const leave = () => { if (hovered) { hovered = false; update(); } };
            on(wrapper, "pointerleave", leave);
            on(wrapper, "pointercancel", leave);
            if (typeof preference.addEventListener === "function") on(preference, "change", update);
            if (wrapper.matches(":hover")) { hovered = true; update(); }
        });

        // Collection and editor use the same gentle shadow, with no size/layout change.
        document.querySelectorAll("[data-category-collection], #category-editor").forEach(card => {
            let hovered = false;
            let focused = card.contains(document.activeElement);
            let shadowAnimation = null;
            const update = () => {
                const active = hovered || focused;
                if (card.classList.contains("shadow") === active) return;
                const previous = window.getComputedStyle(card).boxShadow;
                shadowAnimation?.cancel();
                shadowAnimation = null;
                card.classList.toggle("shadow-sm", !active);
                card.classList.toggle("shadow", active);
                const next = window.getComputedStyle(card).boxShadow;
                if (!preference.matches && typeof card.animate === "function" && previous !== next) {
                    shadowAnimation = animate(card, [{ boxShadow: previous }, { boxShadow: next }],
                        { duration: 180, easing: "ease-out" });
                }
            };
            on(card, "pointerenter", event => {
                if (event.pointerType === "touch") return;
                hovered = true; update();
            });
            const leave = () => { hovered = false; update(); };
            on(card, "pointerleave", leave);
            on(card, "pointercancel", leave);
            on(card, "focusin", () => { focused = true; update(); });
            on(card, "focusout", event => {
                if (event.relatedTarget && card.contains(event.relatedTarget)) return;
                focused = false; update();
            });
            if (card.matches(":hover")) hovered = true;
            update();
        });

        document.querySelectorAll("[data-category-row]").forEach(row => {
            const order = row.querySelector("[data-category-order]");
            const name = row.querySelector("[data-category-row-name]");
            const accent = row.querySelector("[data-category-row-accent]");
            if (!order || !name || !accent) return;
            let hovered = false;
            let focused = row.contains(document.activeElement);
            const updateRow = () => {
                const active = row.getAttribute("data-editing") === "true" || hovered || focused;
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
        if (!form || form.closest("[data-category-drawer]")) return cleanup;

        const nameInput = form.querySelector("[data-category-name]");
        const namePreview = form.querySelector("[data-category-preview-name]");
        const nameCount = form.querySelector("[data-category-name-count]");
        if (!nameInput || !namePreview || !nameCount) return cleanup;

        const updatePreview = () => {
            namePreview.textContent = nameInput.value.trim() ||
                (form.hasAttribute("data-menu-item-form") ? "Your dish name" : "Your category name");
            nameCount.textContent = String(nameInput.value.length);
        };

        on(nameInput, "input", updatePreview);
        updatePreview();

        const panel = document.getElementById("category-editor");
        if (!panel) return cleanup;

        const revealEditor = () => {
            nameInput.focus({ preventScroll: true });
            panel.scrollIntoView({
                behavior: preference.matches ? "auto" : "smooth",
                block: "center",
                inline: "nearest"
            });
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
    const enhanced = !!workspace() && typeof window.fetch === "function" &&
        typeof window.DOMParser === "function" && typeof window.FormData === "function";
    let busy = false;
    let queuedPop = null;

    // Remove the old full-reload scroll record when upgrading from that version.
    try { window.sessionStorage.removeItem("yoru.menuCategories.pendingScroll"); } catch { }

    const captureViewport = element => {
        const anchor = element?.closest?.("#category-editor, #category-collection");
        const pane = window.yoruMenuDrawerLayout?.scrollPane || window.yoruMenuDrawer?.scrollPane;
        return {
            route: route(), y: window.scrollY, paneY: pane ? pane.scrollTop : null,
            anchor: anchor ? anchor.id : null,
            offset: anchor ? (pane?.getBoundingClientRect().top || 0) - anchor.getBoundingClientRect().top : 0
        };
    };
    const validViewport = view => view && Number.isFinite(view.y) &&
        Number.isFinite(view.offset) && view.route === route();
    const restoreViewport = view => {
        if (!view || !Number.isFinite(view.y) || !Number.isFinite(view.offset)) return;
        const anchor = view.anchor === "category-collection" || view.anchor === "category-editor"
            ? document.getElementById(view.anchor) : null;
        const pane = window.yoruMenuDrawerLayout?.scrollPane || window.yoruMenuDrawer?.scrollPane;
        if (pane) {
            const top = Number.isFinite(view.paneY) ? view.paneY : pane.scrollTop;
            pane.scrollTop = Math.max(0, top);
            return;
        }
        const top = Number.isFinite(view.paneY) ? window.scrollY : anchor
            ? window.scrollY + anchor.getBoundingClientRect().top + view.offset : view.y;
        window.scrollTo({ top: Math.max(0, top), behavior: "instant" });
    };
    const saveHistoryViewport = view => {
        window.history.replaceState({
            ...(window.history.state || {}),
            [historyKey]: { ...view, route: route() }
        }, "", window.location.href);
    };
    const ordinaryClick = (event, link) => !event.defaultPrevented && event.button === 0 &&
        !event.ctrlKey && !event.metaKey && !event.shiftKey && !event.altKey &&
        !link.hasAttribute("download") && (!link.target || link.target === "_self");
    const requestError = message => showToast(message, "error");

    const prepareCollectionPhoto = async replacement => {
        const photo = replacement.querySelector(".yoru-menu-collection-photo");
        if (!photo?.getAttribute("src")) return;
        // Keep the current workspace painted while the incoming artwork loads.
        // Mount the same decoded element, rather than starting another async
        // image paint after replacing the workspace with its cream background.
        const ready = new window.Image();
        for (const attribute of photo.attributes) {
            if (attribute.name !== "src") ready.setAttribute(attribute.name, attribute.value);
        }
        ready.loading = "eager";
        ready.decoding = "sync";
        await new Promise(resolve => {
            let settled = false;
            const finish = () => {
                if (settled) return;
                settled = true;
                window.clearTimeout(timer);
                ready.removeEventListener("load", loaded);
                ready.removeEventListener("error", finish);
                resolve();
            };
            const loaded = () => {
                if (typeof ready.decode === "function") ready.decode().then(finish, finish);
                else finish();
            };
            // A broken/slow decorative image must not leave forms locked.
            const timer = window.setTimeout(finish, 4000);
            ready.addEventListener("load", loaded, { once: true });
            ready.addEventListener("error", finish, { once: true });
            ready.src = photo.src;
            if (ready.complete) loaded();
        });
        if (ready.naturalWidth) photo.replaceWith(ready);
    };

    const requestPage = async (destination, {
        method = "GET", body = null, mode = "push", view = null,
        guide = false, focusName = false, trigger = null
    } = {}) => {
        if (busy) return;
        busy = true;
        const oldWorkspace = workspace();
        const position = view || captureViewport(trigger || document.activeElement);
        const focusedId = document.activeElement?.id;
        const fields = [...new Set([
            ...oldWorkspace.querySelectorAll('input:not([type="hidden"]), select, textarea'),
            ...(trigger?.matches?.("[data-menu-filter-form]")
                ? trigger.querySelectorAll('input:not([type="hidden"]), select, textarea') : []),
            ...(window.yoruMenuDrawer?.form?.querySelectorAll('input:not([type="hidden"]), select, textarea') || []),
            ...(window.yoruCategoryDrawer?.form?.querySelectorAll('input:not([type="hidden"]), select, textarea') || [])
        ])].map(element => ({ element, readOnly: element.readOnly, inert: !!element.inert }));
        if (mode !== "pop") saveHistoryViewport(position);
        oldWorkspace.setAttribute("aria-busy", "true");
        const status = oldWorkspace.querySelector("[data-category-live-status]");
        if (status) status.textContent = "Updating menu categories.";
        // Bootstrap gives disabled controls a blue-grey background. Lock editing
        // without toggling that appearance; the busy guard blocks repeat actions.
        fields.forEach(({ element }) => {
            if (element.tagName === "SELECT" || ["checkbox", "radio", "file"].includes(element.type)) element.inert = true;
            else element.readOnly = true;
        });
        try {
            window.yoruMenuDrawer?.setBusy(true);
            window.yoruCategoryDrawer?.setBusy(true);
            if (window.yoruMenuDrawer && !await window.yoruMenuDrawer.guardRequest({ destination, method, trigger })) return;
            if (window.yoruCategoryDrawer && !await window.yoruCategoryDrawer.guardRequest({ destination, method, trigger })) return;
            if (window.yoruCategoryOrder && !await window.yoruCategoryOrder.guardRequest({ destination, method, trigger })) return;
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
            const notices = takeNotices(resultDocument);
            const replacement = document.importNode(candidates[0], true);
            replacement.querySelectorAll("script").forEach(script => script.remove());
            await prepareCollectionPhoto(replacement);
            // Browser Back/Forward may have been requested while a write was in flight.
            // Finish that write, then render the queued GET without replaying the POST.
            if (queuedPop) {
                // A completed save still owns its outcome even when history
                // queues a different list view. Clear a successful create draft
                // before the queued GET, so it cannot be submitted twice.
                if (method === "POST") window.yoruCategoryDrawer?.receiveResponse({
                    replacement: candidates[0], method, trigger,
                    redirected: response.redirected, notices, url: returnedUrl
                });
                if (method === "POST") window.yoruMenuDrawer?.receiveResponse({
                    replacement: candidates[0], method, trigger,
                    redirected: response.redirected, notices, url: returnedUrl
                });
                showNotices(notices);
                return;
            }
            const tabFrom = captureTab();
            disposeEffects();
            const drawerUpdate = window.yoruMenuDrawer?.receiveResponse({
                replacement, method, trigger, redirected: response.redirected, notices, url: returnedUrl
            });
            const categoryDrawerUpdate = window.yoruCategoryDrawer?.receiveResponse({
                replacement, method, trigger, redirected: response.redirected, notices, url: returnedUrl
            });
            oldWorkspace.replaceWith(replacement);
            // Keep the hero/夜 and tab bar mounted. Only section attributes and
            // the New action follow successful saves, navigation and history.
            const returnedControls = resultDocument.querySelector("[data-menu-hero-controls]");
            updateHeroControls(returnedControls);
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
            disposeEffects = initializeEffects({ guide, focusName, tabFrom });
            document.dispatchEvent(new window.CustomEvent("yoru:workspace-updated"));
            window.yoruMenuDrawer?.afterUpdate(drawerUpdate);
            window.yoruCategoryDrawer?.afterUpdate({ ...categoryDrawerUpdate, guide });
            if (!guide && !focusName && !drawerUpdate?.focus && !categoryDrawerUpdate?.focus) {
                const controls = document.querySelector("[data-menu-hero-controls]");
                const inUpdatedView = element => element &&
                    (replacement.contains(element) || controls?.contains(element));
                let target = focusedId ? document.getElementById(focusedId) : null;
                // Completed searches focus the collection heading so the input
                // caret stops blinking. Empty-search reminders still focus the field.
                if (method === "GET" && trigger?.matches?.("[data-menu-search-form]")) {
                    target = replacement.querySelector("#collection-title");
                } else if (trigger?.matches?.("[data-menu-filter-form]")) {
                    // Filter Apply closes its panel; return to the visible toggle.
                    target = replacement.querySelector("[data-menu-filter-toggle]");
                }
                if (!inUpdatedView(target)) {
                    if (trigger?.closest('nav[aria-label="Category pages"], nav[aria-label="Menu item pages"]')) {
                        target = replacement.querySelector('[aria-current="page"]');
                    } else if (trigger?.tagName === "FORM" && method === "GET") {
                        target = replacement.querySelector('form[method="get"] button[type="submit"]');
                    } else if (trigger?.tagName === "A") {
                        target = [...replacement.querySelectorAll("a[data-category-keep-scroll]"),
                        ...(controls?.querySelectorAll("a[data-category-keep-scroll]") || [])]
                            .find(link => link.href === destination.href);
                    } else if (method === "POST") {
                        target = replacement.querySelector("#collection-title");
                    }
                }
                if (inUpdatedView(target)) {
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
            window.yoruMenuDrawer?.setBusy(false);
            window.yoruCategoryDrawer?.setBusy(false);
            fields.forEach(({ element, readOnly, inert }) => {
                if (!element.isConnected) return;
                if (element.tagName === "SELECT" || ["checkbox", "radio", "file"].includes(element.type)) element.inert = inert;
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
        const categoryShortcut = link.hasAttribute("data-category-jump");
        const newItem = link.hasAttribute("data-menu-new-item");
        const isNew = categoryShortcut || newItem;
        // The hero stays mounted while the workspace changes. Build its New
        // destination from the current view so filters and ShowAll stay current.
        if (categoryShortcut) {
            destination.search = window.location.search;
            destination.searchParams.delete("editId");
            destination.searchParams.delete("MenuView");
            destination.searchParams.delete("itemEditId");
            destination.searchParams.delete("Reorder");
            destination.hash = "category-editor";
        }
        const idInput = document.querySelector(categoryShortcut
            ? '[data-category-editor] input[name="Input.Id"]'
            : '[data-menu-item-form] input[name="ItemInput.Id"]');
        if (busy) { event.preventDefault(); return; }
        if (newItem && !enhanced && document.querySelector("[data-menu-drawer]")) return;
        if (isNew && idInput && !idInput.value && !document.querySelector("[data-category-order-form]")) {
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
            guide: isNew, focusName: !isNew && (link.hasAttribute("data-menu-edit") ||
                destination.searchParams.has("editId") && destination.hash === "#category-editor"), trigger: link
        });
    });

    // Confirm through an in-page dialog before the existing submit/fetch path.
    // The original form remains responsible for its ID, token and view state.
    let pendingDeletion = null;
    const approvedDeletes = new WeakSet();
    const confirmDeletion = (form, submitter) => {
        const category = form.dataset.menuDelete === "category";
        const name = form.dataset.menuDeleteName || (category ? "This category" : "This menu item");
        let dialog = document.getElementById("menu-delete-dialog");
        if (!dialog) {
            dialog = document.createElement("dialog");
            dialog.id = "menu-delete-dialog";
            dialog.className = "yoru-item-discard-dialog rounded-4 p-4 shadow-lg";
            dialog.setAttribute("aria-labelledby", "menu-delete-title");
            dialog.setAttribute("aria-describedby", "menu-delete-description");
            dialog.innerHTML = '<div class="d-flex align-items-start justify-content-between gap-3 mb-3">' +
                '<h2 id="menu-delete-title" class="h4 mb-0"></h2>' +
                '<button type="button" class="btn-close flex-shrink-0" aria-label="Cancel deletion" data-delete-close></button></div>' +
                '<p id="menu-delete-description" class="text-body-secondary text-break mb-4">' +
                '<strong class="text-body" data-delete-name></strong> will be permanently deleted. This action cannot be undone.</p>' +
                '<div class="row g-2"><div class="col-6"><button type="button" class="btn btn-outline-secondary rounded-pill w-100 h-100" autofocus data-delete-cancel>Cancel</button></div>' +
                '<div class="col-6"><button type="button" class="btn btn-danger rounded-pill w-100 h-100" data-delete-confirm></button></div></div>';
            const cancel = () => dialog.close("cancel");
            dialog.querySelector("[data-delete-cancel]").addEventListener("click", cancel);
            dialog.querySelector("[data-delete-close]").addEventListener("click", cancel);
            dialog.querySelector("[data-delete-confirm]").addEventListener("click", event => {
                event.currentTarget.disabled = true; dialog.close("delete");
            });
            dialog.addEventListener("cancel", event => { event.preventDefault(); cancel(); });
            dialog.addEventListener("click", event => {
                if (event.target !== dialog) return;
                const box = dialog.getBoundingClientRect();
                if (event.clientX < box.left || event.clientX > box.right || event.clientY < box.top || event.clientY > box.bottom) cancel();
            });
            document.body.append(dialog);
        }
        if (typeof dialog.showModal !== "function" || typeof form.requestSubmit !== "function") {
            return window.confirm('Delete "' + name + '"? This action cannot be undone.');
        }
        dialog.querySelector("#menu-delete-title").textContent = category ? "Delete category?" : "Delete menu item?";
        dialog.querySelector("[data-delete-name]").textContent = name;
        const confirm = dialog.querySelector("[data-delete-confirm]");
        confirm.textContent = category ? "Delete category" : "Delete item"; confirm.disabled = false;
        dialog.returnValue = "";
        return new Promise(resolve => {
            pendingDeletion = { form, dialog };
            const finish = () => {
                const accepted = dialog.returnValue === "delete";
                pendingDeletion = null;
                if (!accepted && submitter?.isConnected) submitter.focus({ preventScroll: true });
                resolve(accepted);
            };
            dialog.addEventListener("close", finish, { once: true });
            try {
                dialog.showModal();
                dialog.querySelector("[data-delete-cancel]").focus({ preventScroll: true });
                if (!window.matchMedia("(prefers-reduced-motion: reduce)").matches && typeof dialog.animate === "function") {
                    dialog.animate([{ opacity: 0, transform: "translateY(.5rem) scale(.97)" },
                    { opacity: 1, transform: "none" }], { duration: 180, easing: "ease-out" });
                }
            } catch {
                dialog.removeEventListener("close", finish);
                if (dialog.open) dialog.close("cancel");
                pendingDeletion = null; resolve(false);
            }
        });
    };
    document.addEventListener("submit", event => {
        const form = event.target;
        if (event.defaultPrevented || !form.matches?.("form[data-menu-delete]")) return;
        if (approvedDeletes.has(form)) { approvedDeletes.delete(form); return; }
        event.preventDefault();
        if (busy || pendingDeletion) return;
        const submitter = event.submitter || form.querySelector('button[type="submit"]');
        const id = form.querySelector('[name="id"]')?.value, action = form.action;
        const choice = confirmDeletion(form, submitter);
        Promise.resolve(choice).then(accepted => {
            if (!accepted || busy || !form.isConnected || form.action !== action || form.querySelector('[name="id"]')?.value !== id) return;
            approvedDeletes.add(form);
            try {
                if (typeof form.requestSubmit === "function") form.requestSubmit(submitter?.form === form ? submitter : undefined);
                else window.HTMLFormElement.prototype.submit.call(form);
            } finally { approvedDeletes.delete(form); }
        });
    }, true);
    const cancelPendingDeletion = () => pendingDeletion?.dialog.close("cancel");
    window.addEventListener("pagehide", cancelPendingDeletion);
    document.addEventListener("yoru:workspace-updated", () => {
        if (pendingDeletion && !pendingDeletion.form.isConnected) cancelPendingDeletion();
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
