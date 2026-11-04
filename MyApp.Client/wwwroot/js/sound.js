// Lucky Lure: all game sound, synthesized with the Web Audio API (no audio files, no libraries).
// Called from C# through Services/SoundService.cs (synchronous in-process interop, so a sound never
// delays the game's own timing). Nothing plays before the first user gesture: the AudioContext is only
// created inside a pointer / key / touch handler (browser autoplay rules).
//
// Graph: voices -> sfx bus ---\
//        music notes -> music bus -> master (volume / mute) -> speakers

const KEY = 'luckylure.sound';
const DEFAULTS = { muted: false, volume: 0.6, music: true };

let ctx = null, master = null, sfx = null, musicBus = null, noiseBuf = null;
let inited = false;
const settings = load();
const voices = new Set();     // one-shot sounds that cut() may silence (skips)
const loops = new Map();      // name -> stop()

// ---------- settings (localStorage is optional: private mode / blocked storage just keeps defaults) ----------
function load() {
  try {
    const s = JSON.parse(localStorage.getItem(KEY) || '{}');
    return {
      muted: typeof s.muted === 'boolean' ? s.muted : DEFAULTS.muted,
      volume: typeof s.volume === 'number' ? Math.min(1, Math.max(0, s.volume)) : DEFAULTS.volume,
      music: typeof s.music === 'boolean' ? s.music : DEFAULTS.music,
    };
  } catch { return { ...DEFAULTS }; }
}
function save() { try { localStorage.setItem(KEY, JSON.stringify(settings)); } catch { /* not persisted */ } }

export function init() {
  if (!inited) {
    inited = true;
    for (const ev of ['pointerdown', 'keydown', 'touchstart'])
      document.addEventListener(ev, unlock, { capture: true, passive: true });
    document.addEventListener('visibilitychange', onVisibility);
  }
}
export const getMuted = () => settings.muted;
export const getVolume = () => settings.volume;
export const getMusic = () => settings.music;

export function setMuted(m) { settings.muted = !!m; save(); applyVolume(); }
export function setVolume(v) { settings.volume = Math.min(1, Math.max(0, +v || 0)); save(); applyVolume(); }
export function setMusic(on) {
  settings.music = !!on; save();
  if (settings.music && musicWanted) startMusicNow(); else fadeMusicOut();
}

function unlock() {
  if (!ctx) {
    const AC = window.AudioContext || window.webkitAudioContext;
    if (!AC) return;
    ctx = new AC();
    master = ctx.createGain();
    master.gain.value = 0;
    master.connect(ctx.destination);
    sfx = ctx.createGain(); sfx.connect(master);
    musicBus = ctx.createGain(); musicBus.gain.value = 0; musicBus.connect(master);
    applyVolume();
  }
  if (ctx.state === 'suspended' && !document.hidden)
    ctx.resume().then(() => { if (musicWanted && settings.music) startMusicNow(); }, () => { });
  else if (musicWanted && settings.music) startMusicNow();
}

// Optional trace for checking timing without speakers: set window.__soundLog = [] in the console.
function trace(kind, name, arg) {
  const log = globalThis.__soundLog;
  if (Array.isArray(log)) log.push([Math.round(performance.now()), kind, name, arg ?? '', ctx ? ctx.state : 'none']);
}

function onVisibility() {
  if (!ctx) return;
  if (document.hidden) ctx.suspend(); else ctx.resume();
}

function applyVolume() {
  if (!ctx) return;
  const target = settings.muted ? 0 : Math.pow(settings.volume, 2) * 0.9; // perceptual curve
  master.gain.cancelScheduledValues(ctx.currentTime);
  master.gain.setTargetAtTime(target, ctx.currentTime, 0.015);              // mute is instant (~50 ms)
}

const ready = () => ctx && ctx.state !== 'closed';

// ---------- building blocks ----------
function track(src, g) {
  const v = { src, g };
  voices.add(v);
  src.onended = () => voices.delete(v);
}

/** One enveloped oscillator note. t = delay (s), a = attack, d = decay, v = peak gain, f2 = glide target. */
function tone({ f, f2, type = 'sine', t = 0, a = 0.005, d = 0.3, v = 0.2, dest, keep = false, vib = 0 }) {
  const t0 = ctx.currentTime + t;
  const o = ctx.createOscillator(), g = ctx.createGain();
  o.type = type;
  o.frequency.setValueAtTime(f, t0);
  if (f2) o.frequency.exponentialRampToValueAtTime(f2, t0 + a + d);
  if (vib) {
    const l = ctx.createOscillator(), lg = ctx.createGain();
    l.frequency.value = 6; lg.gain.value = vib;
    l.connect(lg).connect(o.frequency); l.start(t0); l.stop(t0 + a + d + 0.05);
  }
  g.gain.setValueAtTime(0.0001, t0);
  g.gain.linearRampToValueAtTime(v, t0 + a);
  g.gain.exponentialRampToValueAtTime(0.0001, t0 + a + d);
  o.connect(g).connect(dest || sfx);
  o.start(t0); o.stop(t0 + a + d + 0.05);
  if (!keep) track(o, g);
}

function noiseBuffer() {
  if (!noiseBuf) {
    noiseBuf = ctx.createBuffer(1, ctx.sampleRate, ctx.sampleRate);
    const d = noiseBuf.getChannelData(0);
    for (let i = 0; i < d.length; i++) d[i] = Math.random() * 2 - 1;
  }
  return noiseBuf;
}

/** Filtered noise burst (whoosh, splash, wood knock). */
function noise({ t = 0, a = 0.01, d = 0.3, v = 0.2, type = 'bandpass', f = 1000, f2, q = 1, dest, keep = false }) {
  const t0 = ctx.currentTime + t;
  const s = ctx.createBufferSource(), fl = ctx.createBiquadFilter(), g = ctx.createGain();
  s.buffer = noiseBuffer(); s.loop = true;
  fl.type = type; fl.Q.value = q;
  fl.frequency.setValueAtTime(f, t0);
  if (f2) fl.frequency.exponentialRampToValueAtTime(f2, t0 + a + d);
  g.gain.setValueAtTime(0.0001, t0);
  g.gain.linearRampToValueAtTime(v, t0 + a);
  g.gain.exponentialRampToValueAtTime(0.0001, t0 + a + d);
  s.connect(fl).connect(g).connect(dest || sfx);
  s.start(t0, Math.random() * 0.5); s.stop(t0 + a + d + 0.05);
  if (!keep) track(s, g);
}

/** A soft bell: fundamental + quieter octave partial. */
function bell(f, t = 0, d = 0.5, v = 0.16) {
  tone({ f, t, d, v, type: 'sine' });
  tone({ f: f * 2.01, t, d: d * 0.6, v: v * 0.3, type: 'sine' });
}
const midi = (n) => 440 * Math.pow(2, (n - 69) / 12);

// ---------- one-shot sounds ----------
const SOUNDS = {
  click() { tone({ f: 1700, f2: 1100, type: 'triangle', d: 0.04, v: 0.07 }); },
  spinStart() { noise({ type: 'bandpass', f: 350, f2: 1900, q: 0.8, a: 0.04, d: 0.32, v: 0.16 }); },
  reelStop() {
    tone({ f: 150, f2: 70, d: 0.12, v: 0.32 });                       // wooden body
    noise({ type: 'lowpass', f: 1400, d: 0.05, v: 0.12 });            // knock
    tone({ f: 900, type: 'triangle', d: 0.03, v: 0.05 });             // click
  },
  boat(n) {                                                           // 1st / 2nd / 3rd boat: a step higher each
    const f = [659.3, 784, 987.8][Math.min(2, Math.max(0, (n | 0) - 1))];
    bell(f, 0, 0.55, 0.2);
    tone({ f: f * 1.5, t: 0.06, d: 0.35, v: 0.06 });
    if (n >= 3) for (let i = 0; i < 4; i++) tone({ f: 2000 + i * 400, t: 0.1 + i * 0.05, d: 0.12, v: 0.04 });
  },
  boats3() {                                                          // short fanfare
    [72, 76, 79, 84].forEach((n, i) => tone({ f: midi(n), type: 'triangle', t: i * 0.09, d: 0.25, v: 0.14 }));
    [72, 76, 79].forEach((n) => tone({ f: midi(n + 12), type: 'triangle', t: 0.38, a: 0.02, d: 0.6, v: 0.07, vib: 4 }));
  },
  nudge() {                                                           // rope creak, whip, slide
    for (let i = 0; i < 5; i++) tone({ f: 170 + Math.random() * 60, type: 'sawtooth', t: i * 0.045, d: 0.035, v: 0.035 });
    noise({ type: 'highpass', f: 3500, f2: 900, t: 0.22, d: 0.14, v: 0.13 });
    tone({ f: 320, f2: 170, t: 0.3, d: 0.3, v: 0.08 });
  },
  lineWin(ratio) {                                                    // 1-4 chime notes by win / bet
    const notes = ratio < 1 ? 1 : ratio < 3 ? 2 : ratio < 8 ? 3 : 4;
    [84, 88, 91, 96].slice(0, notes).forEach((n, i) => bell(midi(n), i * 0.075, 0.45, 0.12));
  },
  bigWin() { jingle([72, 76, 79, 84, 79, 84, 88], 0.11, 12); },
  megaWin() { jingle([72, 76, 79, 84, 88, 91, 96, 91, 96, 100], 0.1, 26); },
  rollTicks(ms) {                                                     // counter roll-up, slowing down at the end
    const total = Math.min(4, Math.max(0, ms / 1000));
    for (let t = 0, step = 0.05; t < total; t += step, step *= 1.025)
      tone({ f: 2300, type: 'triangle', t, d: 0.014, v: 0.025 });
  },
  fishLand() {                                                        // bloop + drip
    tone({ f: 280, f2: 760, d: 0.12, v: 0.13 });
    noise({ type: 'lowpass', f: 1300, d: 0.07, v: 0.04 });
  },
  netCast(big) {                                                      // whoosh, then the splash
    noise({ type: 'bandpass', f: 300, f2: 2600, q: 0.7, a: 0.05, d: big ? 0.55 : 0.42, v: 0.18 });
    noise({ type: 'lowpass', f: 3200, f2: 450, t: big ? 0.5 : 0.4, a: 0.01, d: big ? 0.7 : 0.45, v: big ? 0.3 : 0.22 });
    if (big) tone({ f: 95, f2: 42, t: 0.5, d: 0.45, v: 0.35 });
  },
  pluck(i) {                                                          // each fish value: climbs a whole step
    const f = 523.25 * Math.pow(2, Math.min(12, (i | 0) * 2) / 12);
    tone({ f, type: 'triangle', a: 0.003, d: 0.2, v: 0.15 });
    tone({ f: f * 2, type: 'sine', a: 0.003, d: 0.08, v: 0.04 });
  },
  kaching() {                                                         // cash register
    noise({ type: 'highpass', f: 5500, d: 0.07, v: 0.08 });
    bell(1568, 0.02, 0.25, 0.15);
    bell(2093, 0.09, 0.6, 0.15);
  },
  twinkle() { [2093, 2637, 3136, 2637].forEach((f, i) => tone({ f, t: i * 0.045, d: 0.12, v: 0.05 })); },
  slot() { bell(1318.5, 0, 0.3, 0.1); tone({ f: 1975.5, t: 0.05, d: 0.25, v: 0.05 }); },
  levelUp() {
    [67, 71, 74, 79, 83].forEach((n, i) => tone({ f: midi(n), type: 'triangle', t: i * 0.07, d: 0.22, v: 0.13 }));
    [79, 83, 86].forEach((n) => tone({ f: midi(n), type: 'triangle', t: 0.4, a: 0.02, d: 0.8, v: 0.07, vib: 5 }));
    for (let i = 0; i < 8; i++) tone({ f: 2400 + Math.random() * 1600, t: 0.4 + i * 0.06, d: 0.1, v: 0.03 });
  },
  stingerFree() {
    [67, 72, 76, 79].forEach((n, i) => tone({ f: midi(n), type: 'triangle', t: i * 0.12, d: 0.3, v: 0.14 }));
    [72, 76, 79, 84].forEach((n) => tone({ f: midi(n), type: 'triangle', t: 0.5, a: 0.03, d: 0.7, v: 0.06, vib: 4 }));
    tone({ f: midi(48), t: 0.5, d: 0.6, v: 0.15 });
  },
  stingerRetrigger() {
    [74, 79, 83].forEach((n, i) => tone({ f: midi(n), type: 'triangle', t: i * 0.09, d: 0.25, v: 0.14 }));
    [79, 83, 86].forEach((n) => tone({ f: midi(n), type: 'triangle', t: 0.3, a: 0.02, d: 0.55, v: 0.06 }));
  },
  stingerEnd() {
    [79, 76, 74, 72].forEach((n, i) => tone({ f: midi(n), type: 'triangle', t: i * 0.14, d: 0.3, v: 0.12 }));
    [60, 64, 67, 72].forEach((n) => tone({ f: midi(n), type: 'triangle', t: 0.6, a: 0.04, d: 0.9, v: 0.06 }));
  },
};

function jingle(notes, step, coins) {
  notes.forEach((n, i) => tone({ f: midi(n), type: 'triangle', t: i * step, d: 0.22, v: 0.13 }));
  const end = notes.length * step;
  [72, 76, 79, 84].forEach((n) => tone({ f: midi(n), type: 'triangle', t: end, a: 0.02, d: 0.8, v: 0.06, vib: 5 }));
  for (let i = 0; i < coins; i++)                                     // coin sparkle
    tone({ f: 2600 + Math.random() * 2200, t: 0.05 + Math.random() * (end + 0.6), d: 0.08, v: 0.035, type: 'sine' });
}

export function play(name, arg) {
  trace('play', name, arg);
  if (!ready() || ctx.state !== 'running') return;
  const fn = SOUNDS[name];
  if (fn) { try { fn(arg); } catch { /* a missing sound never breaks the game */ } }
}

/** Skip: fade out every one-shot sound and loop still playing (music keeps going). */
export function cut() {
  trace('cut', '');
  if (!ready()) return;
  const t = ctx.currentTime;
  for (const v of voices) {
    try { v.g.gain.cancelScheduledValues(t); v.g.gain.setTargetAtTime(0, t, 0.02); v.src.stop(t + 0.1); } catch { }
  }
  voices.clear();
  stopLoop('tease');
}

// ---------- loops ----------
const LOOPS = {
  whirr() {                                                           // reels spinning: rattling soft noise
    const s = ctx.createBufferSource(), fl = ctx.createBiquadFilter(), g = ctx.createGain();
    const lfo = ctx.createOscillator(), lg = ctx.createGain();
    s.buffer = noiseBuffer(); s.loop = true;
    fl.type = 'bandpass'; fl.frequency.value = 650; fl.Q.value = 1.2;
    lfo.frequency.value = 15; lg.gain.value = 0.02;
    g.gain.value = 0.0001;
    lfo.connect(lg).connect(g.gain);
    s.connect(fl).connect(g).connect(sfx);
    const t = ctx.currentTime;
    g.gain.linearRampToValueAtTime(0.045, t + 0.08);
    s.start(); lfo.start();
    return () => {
      const t2 = ctx.currentTime;
      g.gain.cancelScheduledValues(t2); g.gain.setTargetAtTime(0, t2, 0.05);
      s.stop(t2 + 0.3); lfo.stop(t2 + 0.3);
    };
  },
  tease() {                                                           // suspense: drone + shimmer, slowly rising
    const g = ctx.createGain(); g.gain.value = 0.0001; g.connect(sfx);
    const t = ctx.currentTime;
    const oscs = [[110, 'triangle', 0.5], [164.8, 'triangle', 0.35], [1760, 'sine', 0.08], [2217, 'sine', 0.05]].map(([f, type, v]) => {
      const o = ctx.createOscillator(), og = ctx.createGain();
      o.type = type; o.frequency.setValueAtTime(f, t); o.frequency.linearRampToValueAtTime(f * 1.06, t + 3);
      og.gain.value = v; o.connect(og).connect(g); o.start();
      return o;
    });
    const trem = ctx.createOscillator(), tg = ctx.createGain();
    trem.frequency.value = 7; tg.gain.value = 0.025; trem.connect(tg).connect(g.gain); trem.start();
    g.gain.linearRampToValueAtTime(0.07, t + 0.25);
    return () => {
      const t2 = ctx.currentTime;
      g.gain.cancelScheduledValues(t2); g.gain.setTargetAtTime(0, t2, 0.06);
      for (const o of [...oscs, trem]) o.stop(t2 + 0.4);
    };
  },
};

export function startLoop(name) {
  trace('start', name);
  if (!ready() || ctx.state !== 'running' || loops.has(name) || !LOOPS[name]) return;
  try { loops.set(name, LOOPS[name]()); } catch { }
}
export function stopLoop(name) {
  if (loops.has(name)) trace('stop', name);
  const stop = loops.get(name);
  if (!stop) return;
  loops.delete(name);
  try { stop(); } catch { }
}

// ---------- free-spin music: a light 6/8 sea-shanty loop, scheduled ahead on the audio clock ----------
const EIGHTH = 0.2;                                                    // s per eighth note (dotted quarter = 100)
const MELODY = [                                                       // 8 bars of [midi, eighths]
  [[62, 2], [66, 1], [69, 2], [66, 1]], [[67, 2], [71, 1], [69, 3]],
  [[66, 2], [69, 1], [74, 2], [71, 1]], [[69, 2], [67, 1], [66, 1], [64, 1], [62, 1]],
  [[64, 2], [66, 1], [67, 2], [69, 1]], [[71, 2], [69, 1], [67, 3]],
  [[66, 2], [64, 1], [66, 2], [69, 1]], [[62, 3], [null, 3]],
];
const BASS = [50, 43, 50, 45, 45, 43, 45, 50];

let musicWanted = false, musicTimer = null, musicStopTimer = null, bar = 0, barTime = 0;

export function music(on) {
  trace('music', on ? 'on' : 'off');
  musicWanted = !!on;
  if (musicWanted && settings.music) startMusicNow(); else fadeMusicOut();
}

function startMusicNow() {
  if (!ready() || ctx.state !== 'running') return;                     // starts on unlock instead
  clearTimeout(musicStopTimer); musicStopTimer = null;
  const t = ctx.currentTime;
  musicBus.gain.cancelScheduledValues(t);
  musicBus.gain.setValueAtTime(Math.max(0.0001, musicBus.gain.value), t);
  musicBus.gain.linearRampToValueAtTime(0.55, t + 1.5);               // fade in
  if (musicTimer) return;
  bar = 0; barTime = t + 0.1;
  musicTimer = setInterval(scheduleMusic, 60);
  scheduleMusic();
}

function scheduleMusic() {
  if (!ready()) return;
  if (barTime < ctx.currentTime) barTime = ctx.currentTime + 0.05;   // after a stall: never schedule in the past
  while (barTime < ctx.currentTime + 0.4) {                             // look-ahead: next bar(s) only
    let at = 0;
    for (const [n, len] of MELODY[bar]) {
      if (n) tone({ f: midi(n), type: 'triangle', t: barTime - ctx.currentTime + at * EIGHTH, a: 0.01, d: len * EIGHTH * 0.85, v: 0.11, dest: musicBus, keep: true });
      at += len;
    }
    for (const beat of [0, 3]) {
      tone({ f: midi(BASS[bar]), type: 'sine', t: barTime - ctx.currentTime + beat * EIGHTH, a: 0.01, d: 0.4, v: 0.16, dest: musicBus, keep: true });
      noise({ type: 'highpass', f: 6000, t: barTime - ctx.currentTime + (beat + 1.5) * EIGHTH, d: 0.04, v: 0.025, dest: musicBus, keep: true });
    }
    barTime += 6 * EIGHTH;
    bar = (bar + 1) % MELODY.length;
  }
}

function fadeMusicOut() {
  if (!ready() || !musicTimer || musicStopTimer) return;
  const t = ctx.currentTime;
  musicBus.gain.cancelScheduledValues(t);
  musicBus.gain.setValueAtTime(musicBus.gain.value, t);
  musicBus.gain.linearRampToValueAtTime(0, t + 1.2);                   // fade out
  musicStopTimer = setTimeout(() => {
    clearInterval(musicTimer); musicTimer = null; musicStopTimer = null;
  }, 1300);
}

export function dispose() {
  for (const name of [...loops.keys()]) stopLoop(name);
  clearInterval(musicTimer); musicTimer = null;
}
