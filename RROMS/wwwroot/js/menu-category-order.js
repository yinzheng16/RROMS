// Owner: Member 3. Reorder a complete list; persist only on Save order.
(() => {
    "use strict";
    let active = null, dispose = () => {}, dirty = false;
    const beforeLeave = event => { if (dirty) { event.preventDefault(); event.returnValue = true; } };
    window.addEventListener("beforeunload", beforeLeave);
    let exitDialog = null, pendingExit = null;
    const confirmExit = () => {
        if (pendingExit) return Promise.resolve(false);
        const source = active;
        if (!exitDialog) {
            exitDialog = document.createElement("dialog");
            exitDialog.className = "yoru-item-discard-dialog rounded-4 p-4";
            exitDialog.id = "category-order-exit-dialog";
            exitDialog.setAttribute("aria-labelledby", "category-order-exit-title");
            exitDialog.setAttribute("aria-describedby", "category-order-exit-description");
            exitDialog.innerHTML = '<div class="d-flex align-items-start justify-content-between gap-3 mb-3">' +
                '<h2 id="category-order-exit-title" class="h4 mb-0"></h2>' +
                '<button type="button" class="btn-close" aria-label="Keep arranging" data-order-exit-close></button></div>' +
                '<p id="category-order-exit-description" class="text-body-secondary mb-4"></p>' +
                '<div class="d-flex gap-2"><button type="button" class="btn btn-outline-secondary rounded-pill flex-fill" data-order-exit-keep autofocus>Keep arranging</button>' +
                '<button type="button" class="btn btn-primary rounded-pill flex-fill" data-order-exit-confirm></button></div>';
            exitDialog.querySelector("[data-order-exit-close]").addEventListener("click", () => exitDialog.close("keep"));
            exitDialog.querySelector("[data-order-exit-keep]").addEventListener("click", () => exitDialog.close("keep"));
            exitDialog.querySelector("[data-order-exit-confirm]").addEventListener("click", () => exitDialog.close("exit"));
            document.body.append(exitDialog);
        }
        exitDialog.querySelector("#category-order-exit-title").textContent = dirty ? "Discard the unsaved order?" : "Exit reorder mode?";
        exitDialog.querySelector("#category-order-exit-description").textContent = dirty
            ? "Your latest category movements have not been saved. Leaving will discard these changes."
            : "No order changes have been made. You can return to the category list.";
        exitDialog.querySelector("[data-order-exit-confirm]").textContent = dirty ? "Discard & exit" : "Exit reorder mode";
        if (typeof exitDialog.showModal !== "function")
            return Promise.resolve(window.confirm(exitDialog.querySelector("#category-order-exit-description").textContent));
        return new Promise(resolve => {
            pendingExit = resolve; exitDialog.returnValue = "";
            exitDialog.addEventListener("close", () => {
                const accepted = exitDialog.returnValue === "exit" && active === source && !!source?.isConnected;
                pendingExit = null; resolve(accepted);
            }, { once: true });
            exitDialog.showModal();
        });
    };
    window.yoruCategoryOrder = {
        async guardRequest({ method, trigger }) {
            // Cancel always explains the exit; other navigation warns only if dirty.
            if (method !== "GET" || (!dirty && !trigger?.hasAttribute("data-category-order-cancel"))) return true;
            return await confirmExit();
        }
    };
    window.addEventListener("pagehide", () => { if (exitDialog?.open) exitDialog.close("keep"); });
    const initialize = () => {
        const form = document.querySelector("[data-category-order-form]");
        if (form === active) return;
        dispose(); active = form; dirty = false;
        if (!form) { dispose = () => {}; return; }
        const controller = new window.AbortController();
        const on = (target, type, callback) => target.addEventListener(type, callback, { signal: controller.signal });
        const list = form.querySelector("[data-category-order-list]");
        const rows = () => [...list.querySelectorAll("[data-category-order-row]")];
        const signature = () => JSON.stringify(rows().map(row => row.dataset.categoryOrderRow));
        const initial = signature(); let dragging = null;
        const status = form.querySelector("[data-category-order-status]");
        const locked = () => !!form.closest('[aria-busy="true"]');
        const update = (moved = null) => {
            const all = rows(); dirty = signature() !== initial;
            all.forEach((row, index) => {
                if (moved) row.classList.toggle("is-last-moved", row === moved);
                row.querySelector("[data-category-position]").textContent = String(index + 1).padStart(2, "0");
                row.querySelector('[data-category-move="up"]').disabled = index === 0;
                row.querySelector('[data-category-move="down"]').disabled = index === all.length - 1;
            });
            if (moved) status.textContent = moved.querySelector("[data-category-order-name]").textContent +
                " moved to position " + (all.indexOf(moved) + 1) + ". " + (dirty ? "Select Save order to keep this sequence." : "Original order restored.");
        };
        const move = (row, direction, button) => {
            if (locked()) return;
            const all = rows(), index = all.indexOf(row), target = all[index + direction];
            if (!target) return;
            if (direction < 0) list.insertBefore(row, target); else list.insertBefore(row, target.nextSibling);
            update(row);
            (button?.disabled ? row.querySelector("[data-category-drag]") : button)?.focus({ preventScroll: true });
            row.scrollIntoView?.({ block: "nearest", behavior: "auto" });
        };
        form.querySelectorAll("[data-category-move], [data-category-drag]").forEach(button => { button.hidden = false; });
        on(list, "click", event => {
            const button = event.target.closest("[data-category-move]");
            if (button) move(button.closest("[data-category-order-row]"), button.dataset.categoryMove === "up" ? -1 : 1, button);
        });
        on(list, "keydown", event => {
            const button = event.target.closest("[data-category-drag]");
            if (button && ["ArrowUp", "ArrowDown"].includes(event.key)) {
                event.preventDefault(); move(button.closest("[data-category-order-row]"), event.key === "ArrowUp" ? -1 : 1, button);
            }
        });
        const clearDrag = () => {
            dragging?.classList.remove("is-dragging"); dragging = null;
            rows().forEach(row => row.classList.remove("is-drag-target"));
        };
        on(list, "dragstart", event => {
            const handle = event.target.closest("[data-category-drag]");
            if (!handle || locked()) { event.preventDefault(); return; }
            dragging = handle.closest("[data-category-order-row]"); dragging.classList.add("is-dragging");
            if (event.dataTransfer) {
                event.dataTransfer.effectAllowed = "move";
                event.dataTransfer.setData("text/plain", dragging.dataset.categoryOrderRow);
            }
        });
        on(list, "dragover", event => {
            if (!dragging || locked()) return;
            const target = event.target.closest("[data-category-order-row]");
            if (!target) return;
            event.preventDefault();
            if (event.dataTransfer) event.dataTransfer.dropEffect = "move";
            rows().forEach(row => row.classList.toggle("is-drag-target", row === target && row !== dragging));
        });
        on(list, "drop", event => {
            if (!dragging || locked()) return;
            const target = event.target.closest("[data-category-order-row]");
            if (!target) return;
            event.preventDefault();
            if (target !== dragging) {
                const box = target.getBoundingClientRect();
                list.insertBefore(dragging, event.clientY > box.top + box.height / 2 ? target.nextSibling : target);
                update(dragging); dragging.querySelector("[data-category-drag]")?.focus({ preventScroll: true });
            }
            clearDrag();
        });
        on(list, "dragend", clearDrag);
        update();
        dispose = () => {
            controller.abort(); clearDrag();
            if (exitDialog?.open) exitDialog.close("keep");
        };
    };
    initialize(); document.addEventListener("yoru:workspace-updated", initialize);
})();
