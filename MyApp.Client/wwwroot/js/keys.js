// Lucky Lure: global keyboard shortcuts (Blazor needs JS for document-level key events).
// Space = spin; Space/Enter = dismiss an open overlay or skip a win celebration.
let handler = null;

export function register(dotnet) {
  unregister();
  handler = (e) => {
    const isSpace = e.code === 'Space' || e.key === ' ' || e.key === 'Spacebar';
    const isEnter = e.key === 'Enter';
    if (!isSpace && !isEnter) return;

    if (document.querySelector('.overlay, .celebration')) {
      e.preventDefault();
      // Holding the key must not skip several overlays in a row.
      if (!e.repeat) dotnet.invokeMethodAsync('OnDismissKey');
      return;
    }
    if (!isSpace) return;
    const t = e.target;
    if (t && t.closest && t.closest('input, textarea, select, button, summary, a, [contenteditable]')) return;
    e.preventDefault();
    if (!e.repeat) dotnet.invokeMethodAsync('OnSpaceKey');
  };
  document.addEventListener('keydown', handler);
}

export function unregister() {
  if (handler) document.removeEventListener('keydown', handler);
  handler = null;
}

export function prefersReducedMotion() {
  return !!(window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches);
}
