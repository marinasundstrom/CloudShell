// Pointer handling stays local; only a completed drop changes the Blazor draft.
export function attach(grid, receiver) {
    const abort = new AbortController();
    let drag;
    const clear = () => {
        if (!drag) return;
        const previous = drag;
        drag = null;
        previous.source.classList.remove("dragging");
        previous.target?.classList.remove("drop-before", "drop-after");
        if (grid.hasPointerCapture(previous.pointerId)) grid.releasePointerCapture(previous.pointerId);
    };
    grid.addEventListener("pointerdown", event => {
        const handle = event.target.closest("[data-dashboard-drag-handle]");
        if (!handle || event.button !== 0 || grid.dataset.editing !== "true") return;
        clear();
        drag = {
            source: handle.closest("[data-widget-id]"),
            pointerId: event.pointerId,
            x: event.clientX, y: event.clientY, active: false
        };
        grid.setPointerCapture(event.pointerId);
        event.preventDefault();
    }, { signal: abort.signal });
    grid.addEventListener("pointermove", event => {
        if (!drag || drag.pointerId !== event.pointerId) return;
        if (grid.dataset.editing !== "true") { clear(); return; }
        if (!drag.active && Math.hypot(event.clientX - drag.x, event.clientY - drag.y) < 6) return;
        drag.active = true;
        drag.source.classList.add("dragging");
        drag.target?.classList.remove("drop-before", "drop-after");
        const target = document.elementFromPoint(event.clientX, event.clientY)?.closest("[data-widget-id]");
        drag.target = target && target !== drag.source && grid.contains(target) ? target : null;
        if (drag.target) {
            const bounds = drag.target.getBoundingClientRect();
            drag.after = event.clientY > bounds.top + bounds.height / 2;
            drag.target.classList.add(drag.after ? "drop-after" : "drop-before");
        }
    }, { signal: abort.signal });
    grid.addEventListener("pointerup", event => {
        if (!drag || drag.pointerId !== event.pointerId) return;
        const completed = drag.active && drag.target && grid.dataset.editing === "true"
            ? [drag.source.dataset.widgetId, drag.target.dataset.widgetId, drag.after] : null;
        clear();
        if (completed) receiver.invokeMethodAsync("DropWidget", ...completed).catch(() => {});
    }, { signal: abort.signal });
    grid.addEventListener("pointercancel", clear, { signal: abort.signal });
    grid.addEventListener("lostpointercapture", clear, { signal: abort.signal });
    window.addEventListener("blur", clear, { signal: abort.signal });
    window.addEventListener("keydown", event => { if (event.key === "Escape") clear(); }, { signal: abort.signal });
    return { dispose() { clear(); abort.abort(); } };
}
