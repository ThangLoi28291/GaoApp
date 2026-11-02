(function () {
    "use strict";
    const root = document.querySelector("[data-comparison]");
    if (!root) return;
    const frames = Array.from(root.querySelectorAll("[data-preview-width]"));
    const buttons = Array.from(root.querySelectorAll("[data-comparison-mode]"));
    function resize() {
        frames.forEach(frame => {
            if (!frame.clientWidth) return;
            const width = Number(frame.dataset.previewWidth);
            frame.style.setProperty("--device-width", width + "px");
            frame.style.setProperty("--device-height", frame.dataset.previewHeight + "px");
            frame.style.setProperty("--preview-scale", String(frame.clientWidth / width));
        });
    }
    function selectMode(mode) {
        const button = buttons.find(item => item.dataset.comparisonMode === mode);
        if (!button) return;
        root.dataset.mode = mode;
        buttons.forEach(item => item.setAttribute("aria-pressed", String(item === button)));
        root.querySelectorAll("[data-comparison-panel]").forEach(panel => { panel.hidden = mode !== "both" && panel.dataset.comparisonPanel !== mode; });
        root.querySelector("[data-comparison-status]").textContent = mode === "desktop"
            ? "Bản máy tính tham chiếu ở chiều rộng 1440 px."
            : mode === "phone" ? "Bản điện thoại v2 · 390 px · Dữ liệu minh họa."
                : "Bản máy tính tham chiếu 1440 px và bản điện thoại mới 390 px. Bấm thử riêng trong từng khung.";
        const direct = root.querySelector("[data-comparison-direct]");
        direct.href = mode === "desktop" ? root.dataset.desktopUrl : root.dataset.phoneUrl;
        direct.textContent = mode === "desktop" ? "Mở máy tính trực tiếp ↗" : "Mở điện thoại trực tiếp ↗";
        requestAnimationFrame(resize);
    }
    buttons.forEach(button => button.addEventListener("click", () => selectMode(button.dataset.comparisonMode)));
    const observer = new ResizeObserver(resize);
    frames.forEach(frame => observer.observe(frame));
    selectMode(new URLSearchParams(location.search).get("view") || "phone");
    resize();
})();
