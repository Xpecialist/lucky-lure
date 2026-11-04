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

// ---------- collect flights (free spins) ----------
// Blazor drives the timing; these only spawn a temporary element that flies from one element's centre to
// another's (Web Animations API, transform/opacity only) and removes itself. Elements live in a fixed
// layer on <body>, outside Blazor's DOM.
// A cell's box from the reel window's geometry (reel box, row = a third of its height), not from the strip's
// cell element, so a strip that is still settling cannot throw the flight off.
function cellBox(reel, row) {
  const el = document.querySelectorAll('.reels > .reel')[reel];
  if (!el) return null;
  const b = el.getBoundingClientRect();
  const h = b.height / 3;
  return { getBoundingClientRect: () => ({ left: b.left, top: b.top + row * h, width: b.width, height: h }) };
}

function layer() {
  let el = document.getElementById('fly-layer');
  if (!el) {
    el = document.createElement('div');
    el.id = 'fly-layer';
    el.setAttribute('aria-hidden', 'true');
    document.body.appendChild(el);
  }
  return el;
}

function centre(el) {
  const b = el.getBoundingClientRect();
  return { x: b.left + b.width / 2, y: b.top + b.height / 2, w: b.width, h: b.height };
}

function fly(from, to, node, ms, arc) {
  if (!from || !to) return;
  const a = centre(from), b = centre(to);
  node.style.left = `${a.x}px`;
  node.style.top = `${a.y}px`;
  layer().appendChild(node);
  const dx = b.x - a.x, dy = b.y - a.y;
  const lift = -Math.max(24, Math.abs(dx) * .25) * arc;   // curved path: rise first, then drop in
  const anim = node.animate([
    { transform: 'translate(-50%, -50%) translate(0, 0) scale(1)', opacity: 1 },
    { transform: `translate(-50%, -50%) translate(${dx * .5}px, ${dy * .5 + lift}px) scale(1.15)`, opacity: 1, offset: .5 },
    { transform: `translate(-50%, -50%) translate(${dx}px, ${dy}px) scale(.6)`, opacity: .2 },
  ], { duration: ms, easing: 'cubic-bezier(.45, 0, .55, 1)', fill: 'forwards' });
  anim.onfinish = () => node.remove();
}

/** A Fish's value lifts off its tile and flies to the collecting fisherman's cell. */
export function flyValue(reel, row, toReel, toRow, text, ms) {
  const node = document.createElement('div');
  node.className = 'fly fly-val';
  node.textContent = text;
  const from = cellBox(reel, row);
  const b = from?.getBoundingClientRect();
  // Start where the value is printed: the lower part of the fish tile.
  const start = b && { getBoundingClientRect: () => ({ left: b.left, top: b.top + b.height * .62, width: b.width, height: b.height * .3 }) };
  fly(start, cellBox(toReel, toRow), node, ms, 1);
}

/** A sparkling fisherman flies from his cell up to a slot of the counter bar (or the bar if none). */
export function flySpark(reel, row, slot, ms) {
  const node = document.createElement('div');
  node.className = 'fly fly-spark';
  node.innerHTML = '<svg viewBox="0 0 100 100"><use href="#sym-wild"></use></svg>';
  const target = document.querySelector(`.fs-slot[data-i="${slot}"]`) ?? document.querySelector('.fs-bar');
  fly(cellBox(reel, row), target, node, ms, .6);
}

/** Skip: remove every flight still in the air. */
export function clearFlights() {
  document.getElementById('fly-layer')?.replaceChildren();
}
