// Shows a chart's value tooltip. Uses delegation so it survives Blazor re-rendering the charts.
(() => {
    const tip = document.createElement("div");
    tip.className = "chart-tip";

    document.addEventListener("pointerover", (event) => {
        const text = event.target.dataset?.tip;
        if (!text) {
            tip.remove();
            return;
        }
        tip.textContent = text;
        if (!tip.isConnected) document.body.append(tip);
    });

    document.addEventListener("pointermove", (event) => {
        if (!tip.isConnected) return;
        const maxLeft = window.innerWidth - tip.offsetWidth - 8;
        tip.style.left = `${Math.max(8, Math.min(event.clientX + 12, maxLeft))}px`;
        tip.style.top = `${event.clientY - tip.offsetHeight - 12}px`;
    });
})();
