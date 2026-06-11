(function () {
    window.domainObjectEditorPanes = window.domainObjectEditorPanes || {
        initialize: function (layout) {
            if (!layout) {
                return;
            }

            bindSplitter(layout, ".cte-horizontal-splitter", "horizontal");
            bindSplitter(layout, ".cte-vertical-splitter", "vertical");
        }
    };

    function bindSplitter(layout, selector, direction) {
        const splitter = layout.querySelector(selector);
        if (!splitter || splitter.dataset.domainObjectEditorBound === "true") {
            return;
        }

        splitter.dataset.domainObjectEditorBound = "true";
        splitter.addEventListener("pointerdown", function (event) {
            event.preventDefault();
            event.stopPropagation();

            const computed = getComputedStyle(layout);
            const startX = event.clientX;
            const startY = event.clientY;
            const startInspectorWidth = layout.querySelector(".cte-inspector")?.getBoundingClientRect().width
                ?? parsePixels(computed.getPropertyValue("--cte-inspector-width"), 680);
            const startRelationshipHeight = parsePixels(computed.getPropertyValue("--cte-relationship-height"), 220);

            layout.classList.add(direction === "horizontal" ? "cte-resizing-horizontal" : "cte-resizing-vertical");
            document.body.classList.add("cte-pane-resize-active");

            const move = function (moveEvent) {
                moveEvent.preventDefault();

                if (direction === "horizontal") {
                    const nextWidth = clamp(startInspectorWidth - (moveEvent.clientX - startX), 320, 1100);
                    layout.style.setProperty("--cte-inspector-width", nextWidth + "px");
                    return;
                }

                const nextHeight = clamp(startRelationshipHeight - (moveEvent.clientY - startY), 120, 420);
                layout.style.setProperty("--cte-relationship-height", nextHeight + "px");
            };

            const stop = function () {
                layout.classList.remove("cte-resizing-horizontal", "cte-resizing-vertical");
                document.body.classList.remove("cte-pane-resize-active");
                window.removeEventListener("pointermove", move);
                window.removeEventListener("pointerup", stop);
                window.removeEventListener("pointercancel", stop);
            };

            window.addEventListener("pointermove", move, { passive: false });
            window.addEventListener("pointerup", stop, { once: true });
            window.addEventListener("pointercancel", stop, { once: true });
        });
    }

    function parsePixels(value, fallback) {
        const parsed = Number.parseFloat(value);
        return Number.isFinite(parsed) ? parsed : fallback;
    }

    function clamp(value, min, max) {
        return Math.min(max, Math.max(min, value));
    }
})();
