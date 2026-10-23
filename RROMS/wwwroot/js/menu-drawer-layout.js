// Owner: Member 3. Only the lower collection shares a row with the editor.
(() => {
    "use strict";
    let panel = null;
    const studio = () => document.querySelector("[data-menu-studio]");
    const desktopLayout = window.matchMedia?.("(min-width: 87.5rem)");
    // Both desktop columns expand in normal document flow.
    const scrollPane = () => null;
    const clearPlacement = () => {
        ["--yoru-pin-top", "--yoru-pin-left", "--yoru-pin-width", "--yoru-pin-height"]
            .forEach(name => panel?.style.removeProperty(name));
    };
    const leaveSplit = () => {
        studio()?.classList.remove("is-split");
        studio()?.style.removeProperty("--yoru-split-height");
    };
    const placeEditor = () => {
        const root = studio(), pane = root?.querySelector("[data-menu-content]");
        if (!panel || !root?.classList.contains("is-editor-open") || !desktopLayout?.matches || !pane) {
            leaveSplit(); clearPlacement(); return;
        }
        clearPlacement();
        root.classList.add("is-split");
    };
    window.yoruMenuDrawerLayout = {
        get scrollPane() { return scrollPane(); },
        activate(next) {
            if (!next) return;
            const previous = panel;
            panel = next;
            if (previous && previous !== next) {
                previous.classList.remove("is-open");
                previous.inert = true;
                previous.setAttribute("aria-hidden", "true");
            }
            studio()?.classList.add("is-editor-open");
            placeEditor();
            document.dispatchEvent(new window.CustomEvent("yoru:drawer-activated", { detail: { panel: next } }));
        },
        replace(previous, next) { if (panel === previous) panel = next; },
        deactivate(current) {
            if (panel !== current) return;
            leaveSplit(); clearPlacement(); panel = null;
            studio()?.classList.remove("is-editor-open");
        },
        reveal(current) {
            if (panel !== current || !desktopLayout?.matches) return;
            const unit = parseFloat(window.getComputedStyle(document.documentElement).fontSize) || 16;
            const top = window.scrollY + current.getBoundingClientRect().top - 1.5 * unit;
            window.scrollTo({ top: Math.max(0, top), behavior:
                window.matchMedia?.("(prefers-reduced-motion: reduce)").matches ? "instant" : "smooth" });
        },
        refresh: placeEditor
    };
    window.addEventListener("resize", placeEditor);
    window.visualViewport?.addEventListener("resize", placeEditor);
    desktopLayout?.addEventListener("change", placeEditor);
    document.addEventListener("yoru:workspace-updated", placeEditor);
    if (typeof window.ResizeObserver === "function") {
        const observer = new window.ResizeObserver(placeEditor);
        const header = document.querySelector("body > header");
        if (header) observer.observe(header);
    }
})();
