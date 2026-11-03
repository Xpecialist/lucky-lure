// Lucky Lure: win counter roll-up (one requestAnimationFrame loop per big win).
// The element is left empty by Blazor, so writing its text here never fights the renderer.
const fmt = new Intl.NumberFormat('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 });

export function rollUp(el, to, ms) {
  if (!el) return;
  if (!(ms > 0)) { el.textContent = fmt.format(to); return; }
  const start = performance.now();
  const step = (now) => {
    if (!el.isConnected) return;
    const t = Math.min(1, (now - start) / ms);
    const eased = 1 - Math.pow(1 - t, 3); // ease-out cubic
    el.textContent = fmt.format(to * eased);
    if (t < 1) requestAnimationFrame(step);
  };
  requestAnimationFrame(step);
}
