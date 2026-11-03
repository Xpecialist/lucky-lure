namespace MyApp.Client.Game;

/// <summary>
/// Original cartoon artwork for the 12 symbols as one inline SVG sprite (100 x 100 per symbol).
/// Rendered once by <c>SymbolSprite.razor</c>; cells reference it with <c>&lt;use href="#sym-fish"/&gt;</c>,
/// which keeps every reel cell to two tiny DOM nodes. Kept as a string (rendered as MarkupString) because
/// Razor treats <c>&lt;text&gt;</c> as its own tag and the browser's HTML parser handles SVG casing.
/// </summary>
public static class SymbolArt
{
    public const string Sprite = """
<svg xmlns="http://www.w3.org/2000/svg" width="0" height="0" style="position:absolute;width:0;height:0;overflow:hidden" aria-hidden="true" focusable="false">
  <defs>
    <linearGradient id="g-teal" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#6ff2dc"/><stop offset="1" stop-color="#0f7a69"/></linearGradient>
    <linearGradient id="g-blue" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#8cc4ff"/><stop offset="1" stop-color="#1d48b0"/></linearGradient>
    <linearGradient id="g-pink" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#ff9fe6"/><stop offset="1" stop-color="#9c1d84"/></linearGradient>
    <linearGradient id="g-red" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#ff9a86"/><stop offset="1" stop-color="#b0231a"/></linearGradient>
    <linearGradient id="g-gold" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#fff0a8"/><stop offset=".5" stop-color="#f6c33b"/><stop offset="1" stop-color="#c07a00"/></linearGradient>
    <linearGradient id="g-green" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#6be8a4"/><stop offset="1" stop-color="#1b8a50"/></linearGradient>
    <linearGradient id="g-orange" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#ffd166"/><stop offset=".55" stop-color="#ff8f1f"/><stop offset="1" stop-color="#e0560b"/></linearGradient>
    <linearGradient id="g-wing" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#f2fbff" stop-opacity=".95"/><stop offset="1" stop-color="#8fd3f5" stop-opacity=".8"/></linearGradient>
    <linearGradient id="g-dfbody" x1="0" y1="0" x2="1" y2="0"><stop offset="0" stop-color="#1a5fae"/><stop offset=".5" stop-color="#3fd6c6"/><stop offset="1" stop-color="#1a5fae"/></linearGradient>
    <linearGradient id="g-silver" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#ffffff"/><stop offset="1" stop-color="#8d9aa6"/></linearGradient>
    <linearGradient id="g-hat" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#7fcf6a"/><stop offset="1" stop-color="#2f7d3a"/></linearGradient>
    <linearGradient id="g-hull" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#ff7466"/><stop offset="1" stop-color="#a81e14"/></linearGradient>
    <!-- Vertical-only motion blur for spinning strips. -->
    <filter id="reel-blur" x="-5%" y="-10%" width="110%" height="120%"><feGaussianBlur stdDeviation="0 3"/></filter>
  </defs>

  <!-- Card symbols: a letter on a coloured gem, a different shape each. -->
  <symbol id="sym-ten" viewBox="0 0 100 100">
    <path d="M50 6 L89 25 L89 71 L50 94 L11 71 L11 25 Z" fill="url(#g-teal)" stroke="#fff" stroke-width="4" stroke-linejoin="round"/>
    <path d="M50 13 L82 29 L82 43 Q50 55 18 43 L18 29 Z" fill="#fff" opacity=".22"/>
    <text x="50" y="66" text-anchor="middle" font-family="Georgia,'Times New Roman',serif" font-weight="900" font-size="44" letter-spacing="-3" fill="#fff" stroke="#0a4a40" stroke-width="5" stroke-linejoin="round" paint-order="stroke">10</text>
  </symbol>
  <symbol id="sym-jack" viewBox="0 0 100 100">
    <rect x="12" y="10" width="76" height="80" rx="18" fill="url(#g-blue)" stroke="#fff" stroke-width="4"/>
    <path d="M20 22 Q50 12 80 22 L80 42 Q50 52 20 42 Z" fill="#fff" opacity=".22"/>
    <text x="50" y="68" text-anchor="middle" font-family="Georgia,'Times New Roman',serif" font-weight="900" font-size="52" fill="#fff" stroke="#122e75" stroke-width="5" stroke-linejoin="round" paint-order="stroke">J</text>
  </symbol>
  <symbol id="sym-queen" viewBox="0 0 100 100">
    <circle cx="50" cy="50" r="40" fill="url(#g-pink)" stroke="#fff" stroke-width="4"/>
    <ellipse cx="50" cy="32" rx="28" ry="14" fill="#fff" opacity=".22"/>
    <text x="50" y="68" text-anchor="middle" font-family="Georgia,'Times New Roman',serif" font-weight="900" font-size="50" fill="#fff" stroke="#6a0f58" stroke-width="5" stroke-linejoin="round" paint-order="stroke">Q</text>
  </symbol>
  <symbol id="sym-king" viewBox="0 0 100 100">
    <path d="M14 14 Q50 4 86 14 L86 56 Q86 80 50 94 Q14 80 14 56 Z" fill="url(#g-red)" stroke="#fff" stroke-width="4" stroke-linejoin="round"/>
    <path d="M21 20 Q50 12 79 20 L79 40 Q50 50 21 40 Z" fill="#fff" opacity=".22"/>
    <text x="50" y="68" text-anchor="middle" font-family="Georgia,'Times New Roman',serif" font-weight="900" font-size="50" fill="#fff" stroke="#7a130c" stroke-width="5" stroke-linejoin="round" paint-order="stroke">K</text>
  </symbol>
  <symbol id="sym-ace" viewBox="0 0 100 100">
    <path d="M50 5 L93 50 L50 95 L7 50 Z" fill="url(#g-gold)" stroke="#fff" stroke-width="4" stroke-linejoin="round"/>
    <path d="M50 13 L80 44 Q50 52 20 44 Z" fill="#fff" opacity=".3"/>
    <text x="50" y="68" text-anchor="middle" font-family="Georgia,'Times New Roman',serif" font-weight="900" font-size="48" fill="#fff" stroke="#8a5300" stroke-width="5" stroke-linejoin="round" paint-order="stroke">A</text>
  </symbol>

  <!-- Tackle box with a lure peeking out. -->
  <symbol id="sym-tacklebox" viewBox="0 0 100 100">
    <path d="M37 25 Q50 9 63 25" fill="none" stroke="#5b3a1a" stroke-width="6" stroke-linecap="round"/>
    <ellipse cx="74" cy="20" rx="10" ry="5" transform="rotate(-25 74 20)" fill="#ff5a4a" stroke="#7a1a10" stroke-width="2"/>
    <path d="M66 24 l-4 5" stroke="#555" stroke-width="2" stroke-linecap="round"/>
    <circle cx="80" cy="16.5" r="1.8" fill="#fff"/>
    <rect x="13" y="25" width="74" height="20" rx="6" fill="url(#g-green)" stroke="#0e4d2c" stroke-width="3"/>
    <rect x="9" y="42" width="82" height="46" rx="8" fill="url(#g-green)" stroke="#0e4d2c" stroke-width="3"/>
    <rect x="16" y="50" width="68" height="15" rx="3" fill="#0e4d2c" opacity=".3"/>
    <rect x="16" y="69" width="68" height="13" rx="3" fill="#0e4d2c" opacity=".3"/>
    <rect x="43" y="55.5" width="14" height="4" rx="2" fill="#e8fff2"/>
    <rect x="43" y="73.5" width="14" height="4" rx="2" fill="#e8fff2"/>
    <rect x="43" y="37" width="14" height="11" rx="2" fill="url(#g-gold)" stroke="#7a5200" stroke-width="2"/>
    <path d="M17 30 h22" stroke="#fff" stroke-width="3" stroke-linecap="round" opacity=".45"/>
  </symbol>

  <!-- Dragonfly seen from above. -->
  <symbol id="sym-dragonfly" viewBox="0 0 100 100">
    <g fill="url(#g-wing)" stroke="#2a7fb8" stroke-width="2">
      <ellipse cx="29" cy="35" rx="25" ry="9" transform="rotate(-14 29 35)"/>
      <ellipse cx="71" cy="35" rx="25" ry="9" transform="rotate(14 71 35)"/>
      <ellipse cx="31" cy="50" rx="21" ry="7.5" transform="rotate(16 31 50)"/>
      <ellipse cx="69" cy="50" rx="21" ry="7.5" transform="rotate(-16 69 50)"/>
    </g>
    <g stroke="#7cc3ea" stroke-width="1.2" fill="none">
      <path d="M44 37 L10 31 M44 47 L14 55 M56 37 L90 31 M56 47 L86 55"/>
    </g>
    <path d="M46.5 44 Q50 41 53.5 44 L52.4 89 Q50 95 47.6 89 Z" fill="url(#g-dfbody)" stroke="#123f5e" stroke-width="2"/>
    <path d="M47 55 h6 M47.2 63 h5.6 M47.5 71 h5 M47.8 79 h4.4" stroke="#123f5e" stroke-width="1.6"/>
    <ellipse cx="50" cy="38" rx="8" ry="10" fill="url(#g-dfbody)" stroke="#123f5e" stroke-width="2"/>
    <circle cx="50" cy="23" r="7" fill="#2bb5b0" stroke="#123f5e" stroke-width="2"/>
    <circle cx="44.5" cy="20" r="5.5" fill="#1b2b6b"/>
    <circle cx="55.5" cy="20" r="5.5" fill="#1b2b6b"/>
    <circle cx="43" cy="18.5" r="1.8" fill="#fff"/>
    <circle cx="54" cy="18.5" r="1.8" fill="#fff"/>
  </symbol>

  <!-- Fishing rod with reel, line and hook. -->
  <symbol id="sym-rod" viewBox="0 0 100 100">
    <path d="M83 13 L83 60" stroke="#35607f" stroke-width="1.6"/>
    <path d="M83 60 v9 a6 6 0 1 1 -10 -3" fill="none" stroke="#5d6770" stroke-width="3.2" stroke-linecap="round"/>
    <path d="M70 64 l3 2" stroke="#5d6770" stroke-width="3" stroke-linecap="round"/>
    <path d="M17 91 Q38 42 83 13" fill="none" stroke="#8a5328" stroke-width="6" stroke-linecap="round"/>
    <path d="M18 88 Q36 46 80 16" fill="none" stroke="#d9a066" stroke-width="1.8" stroke-linecap="round" opacity=".8"/>
    <circle cx="56" cy="32" r="2.6" fill="none" stroke="#c9d3db" stroke-width="1.8"/>
    <circle cx="70" cy="22" r="2.2" fill="none" stroke="#c9d3db" stroke-width="1.6"/>
    <path d="M17 91 L28 73" stroke="#2b2b2b" stroke-width="11" stroke-linecap="round"/>
    <circle cx="36" cy="70" r="10" fill="url(#g-silver)" stroke="#444" stroke-width="2.5"/>
    <circle cx="36" cy="70" r="3.2" fill="#444"/>
    <path d="M36 70 L45 62" stroke="#444" stroke-width="2.5" stroke-linecap="round"/>
    <circle cx="45" cy="62" r="2.6" fill="#c8362a"/>
  </symbol>

  <!-- Red and white life ring. -->
  <symbol id="sym-float" viewBox="0 0 100 100">
    <circle cx="50" cy="50" r="31" fill="none" stroke="#ffffff" stroke-width="21"/>
    <circle cx="50" cy="50" r="31" fill="none" stroke="#e8352a" stroke-width="21" stroke-dasharray="24.35 24.35" transform="rotate(-11 50 50)"/>
    <circle cx="50" cy="50" r="41.5" fill="none" stroke="#8a1a12" stroke-width="2.5"/>
    <circle cx="50" cy="50" r="20.5" fill="none" stroke="#8a1a12" stroke-width="2.5"/>
    <circle cx="50" cy="50" r="31" fill="none" stroke="#c9a46a" stroke-width="2.4" stroke-dasharray="5 3.5"/>
    <path d="M24 34 A30 30 0 0 1 40 20" fill="none" stroke="#fff" stroke-width="4" stroke-linecap="round" opacity=".7"/>
  </symbol>

  <!-- Fish (its cash value badge is drawn by the cell). -->
  <symbol id="sym-fish" viewBox="0 0 100 100">
    <path d="M30 32 Q45 13 63 31 Z" fill="#ff9d2e" stroke="#8a3a00" stroke-width="3" stroke-linejoin="round"/>
    <path d="M70 50 L93 31 Q86 50 93 69 Z" fill="url(#g-orange)" stroke="#8a3a00" stroke-width="3" stroke-linejoin="round"/>
    <ellipse cx="45" cy="50" rx="33" ry="22" fill="url(#g-orange)" stroke="#8a3a00" stroke-width="3"/>
    <ellipse cx="44" cy="58" rx="23" ry="9" fill="#ffe7b5" opacity=".75"/>
    <g fill="none" stroke="#c8540b" stroke-width="1.8" opacity=".75">
      <path d="M44 38 q5 5 0 10 M54 38 q5 5 0 10 M49 46 q5 5 0 10 M59 46 q5 5 0 10"/>
    </g>
    <path d="M42 55 q10 3 6 13 q-9 -4 -6 -13 Z" fill="#ff8a1f" stroke="#8a3a00" stroke-width="2" stroke-linejoin="round"/>
    <circle cx="24" cy="45" r="6.5" fill="#fff" stroke="#3a1a00" stroke-width="2"/>
    <circle cx="22.5" cy="45" r="3.2" fill="#1a1a1a"/>
    <circle cx="21.5" cy="43.8" r="1" fill="#fff"/>
    <path d="M13 55 q4 2.5 8 0" fill="none" stroke="#8a3a00" stroke-width="2.4" stroke-linecap="round"/>
  </symbol>

  <!-- WILD: the bearded fisherman in a bucket hat. -->
  <symbol id="sym-wild" viewBox="0 0 100 100">
    <circle cx="50" cy="55" r="23" fill="#ffd2a6" stroke="#7a4a22" stroke-width="3"/>
    <path d="M28 57 Q30 88 50 90 Q70 88 72 57 Q61 68 50 68 Q39 68 28 57 Z" fill="#f1f1f1" stroke="#8a8a8a" stroke-width="2.5" stroke-linejoin="round"/>
    <path d="M37 63 Q43.5 56.5 50 61.5 Q56.5 56.5 63 63 Q56.5 67.5 50 64.5 Q43.5 67.5 37 63 Z" fill="#d6d6d6" stroke="#8a8a8a" stroke-width="1.5"/>
    <ellipse cx="50" cy="57" rx="4.2" ry="3.6" fill="#f0a27c"/>
    <circle cx="41.5" cy="50" r="3.1" fill="#222"/>
    <circle cx="58.5" cy="50" r="3.1" fill="#222"/>
    <circle cx="40.6" cy="49" r="1" fill="#fff"/>
    <circle cx="57.6" cy="49" r="1" fill="#fff"/>
    <circle cx="35" cy="57" r="3.5" fill="#ff9a8a" opacity=".55"/>
    <circle cx="65" cy="57" r="3.5" fill="#ff9a8a" opacity=".55"/>
    <path d="M27 36 Q29 9 50 9 Q71 9 73 36 Z" fill="url(#g-hat)" stroke="#123d18" stroke-width="3" stroke-linejoin="round"/>
    <path d="M28.5 28 Q50 23 71.5 28 L72.5 34 Q50 29 27.5 34 Z" fill="#c8362a"/>
    <ellipse cx="50" cy="37" rx="38" ry="8" fill="#3b8f43" stroke="#123d18" stroke-width="3"/>
    <circle cx="64" cy="22" r="4" fill="url(#g-gold)" stroke="#7a5200" stroke-width="1.5"/>
    <path d="M64 26 v5" stroke="#555" stroke-width="1.6"/>
  </symbol>

  <!-- SCATTER: a little fishing boat on the waves. -->
  <symbol id="sym-scatter" viewBox="0 0 100 100">
    <path d="M50 12 L50 58" stroke="#5b3a1a" stroke-width="4" stroke-linecap="round"/>
    <path d="M50 13 L70 20 L50 27 Z" fill="#ffcf3a" stroke="#8a5a00" stroke-width="1.5" stroke-linejoin="round"/>
    <path d="M50 30 L50 56 L28 56 Z" fill="#ffffff" stroke="#24476b" stroke-width="2.5" stroke-linejoin="round"/>
    <rect x="54" y="40" width="22" height="18" rx="3" fill="#fff" stroke="#24476b" stroke-width="3"/>
    <rect x="58" y="44.5" width="6" height="6" rx="1" fill="#7cc8f2"/>
    <rect x="66.5" y="44.5" width="6" height="6" rx="1" fill="#7cc8f2"/>
    <path d="M10 58 L90 58 L77 79 L23 79 Z" fill="url(#g-hull)" stroke="#5a1410" stroke-width="3" stroke-linejoin="round"/>
    <path d="M15 64 L85 64" stroke="#fff" stroke-width="4"/>
    <circle cx="30" cy="71" r="2.4" fill="#ffe0d8"/>
    <path d="M5 86 q8 -7 16 0 t16 0 t16 0 t16 0 t16 0 t16 0" fill="none" stroke="#2a8fd6" stroke-width="5" stroke-linecap="round"/>
  </symbol>
</svg>
""";
}
