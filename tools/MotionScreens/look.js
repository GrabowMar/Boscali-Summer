// Shared look for every motion screen: stage (camera rig, time warp, HUD, post), palette, type and drawing helpers.
// World units are design pixels of a 1280x720 frame at z = 0 (fov 30, camera at DIST), origin at frame centre, y up.
// ?res=1080 renders the same design at 1920x1080 (PX = output pixels per design pixel).
import * as THREE from 'three';
import { Line2 } from 'three/addons/lines/Line2.js';
import { LineGeometry } from 'three/addons/lines/LineGeometry.js';
import { LineMaterial } from 'three/addons/lines/LineMaterial.js';

THREE.ColorManagement.enabled = false; // colours are screen values, blended like a 2D design tool

export { THREE };
export const W = 1280, H = 720, FPS = 30;
export const PX = (Number(new URLSearchParams(globalThis.location?.search ?? '').get('res')) || H) / H;
// Full-screen layout (the game fills the screen and may crop top and bottom on wide screens; its caption covers the bottom 12 %):
// key content inside SAFE (design px from centre), nothing important below CALM.
export const SAFE = { x: 560, top: 270 }, CALM = -250;
export const DIST = (H / 2) / Math.tan(THREE.MathUtils.degToRad(15));
export const C = { bg: '#05080d', ink: '#e6e1d3', amber: '#ea962e', red: '#e0453a', blue: '#6fa8dc', land: '#0a0f17' };
export const MINUS = '−', NNBSP = ' '; // typographic minus; narrow no-break space before %
export const FONT = {
  cond: '"Bahnschrift Condensed", Bahnschrift, "Segoe UI", sans-serif',
  sans: 'Bahnschrift, "Segoe UI", sans-serif',
  mono: 'Consolas, "Cascadia Mono", monospace',
};

// ---- maths -------------------------------------------------------------------------------------
export const clamp = (x, a = 0, b = 1) => Math.min(b, Math.max(a, x));
export const seg = (t, a, b) => clamp((t - a) / (b - a));
export const lerp = (a, b, k) => a + (b - a) * k;
export const E = {
  inOut: k => (k < 0.5 ? 4 * k * k * k : 1 - Math.pow(-2 * k + 2, 3) / 2),
  sine: k => 0.5 - 0.5 * Math.cos(Math.PI * k),
  out: k => 1 - Math.pow(1 - k, 3),
  outExpo: k => (k >= 1 ? 1 : 1 - Math.pow(2, -10 * k)),
  in: k => k * k * k,
  inExpo: k => (k <= 0 ? 0 : Math.pow(2, 10 * k - 10)),
  outBack: k => 1 + 2.70158 * Math.pow(k - 1, 3) + 1.70158 * Math.pow(k - 1, 2),
};
export function rng(seed) { // mulberry32
  let a = seed >>> 0;
  return () => {
    a = (a + 0x6d2b79f5) >>> 0;
    let t = a;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}
export const hash = (a, b = 0) => { // deterministic 0..1 from two numbers
  const s = Math.sin(a * 127.1 + b * 311.7) * 43758.5453;
  return s - Math.floor(s);
};
export const noise1 = (x, s = 0) => { // smooth value noise, 0..1
  const i = Math.floor(x), f = x - i, u = f * f * (3 - 2 * f);
  return lerp(hash(i, s), hash(i + 1, s), u);
};
export const col = (hex) => new THREE.Color(hex);
export const mixCol = (a, b, k, out = new THREE.Color()) => out.copy(col(a)).lerp(col(b), clamp(k));

// ---- drawing helpers ---------------------------------------------------------------------------
const flat = (pts) => pts.flatMap(p => [p[0], p[1], p[2] ?? 0]);

/** Polyline. width 1 = GL line (optionally dashed), width > 1 = Line2 in screen pixels. `.reveal(n)` draws the first n points. */
export function line(pts, { color = C.ink, opacity = 1, width = 1, dashed = false, dash = 6, gap = 6, closed = false, additive = false } = {}) {
  if (closed) pts = [...pts, pts[0]];
  let obj;
  if (width > 1) {
    const g = new LineGeometry(); g.setPositions(flat(pts));
    const m = new LineMaterial({ color, linewidth: width * PX, transparent: true, opacity, depthTest: false, depthWrite: false });
    obj = new Line2(g, m);
    obj.reveal = n => { g.instanceCount = Math.max(0, Math.min(pts.length, n) - 1); };
  } else {
    const g = new THREE.BufferGeometry(); g.setAttribute('position', new THREE.Float32BufferAttribute(flat(pts), 3));
    const m = dashed
      ? new THREE.LineDashedMaterial({ color, dashSize: dash, gapSize: gap, transparent: true, opacity, depthTest: false, depthWrite: false })
      : new THREE.LineBasicMaterial({ color, transparent: true, opacity, depthTest: false, depthWrite: false });
    if (additive) m.blending = THREE.AdditiveBlending;
    obj = new THREE.Line(g, m);
    if (dashed) obj.computeLineDistances();
    obj.reveal = n => g.setDrawRange(0, Math.max(0, n));
  }
  obj.frustumCulled = false;
  return obj;
}

/** Many independent segments with per-vertex colours (fade a segment by darkening its colour; additive by default). */
export function segments(maxSegs, { additive = true, opacity = 1 } = {}) {
  const g = new THREE.BufferGeometry();
  const pos = new Float32Array(maxSegs * 6), cl = new Float32Array(maxSegs * 6);
  g.setAttribute('position', new THREE.BufferAttribute(pos, 3).setUsage(THREE.DynamicDrawUsage));
  g.setAttribute('color', new THREE.BufferAttribute(cl, 3).setUsage(THREE.DynamicDrawUsage));
  const m = new THREE.LineBasicMaterial({ vertexColors: true, transparent: true, opacity, depthTest: false, depthWrite: false,
    blending: additive ? THREE.AdditiveBlending : THREE.NormalBlending });
  const obj = new THREE.LineSegments(g, m);
  obj.frustumCulled = false;
  let n = 0;
  obj.begin = () => { n = 0; };
  obj.push = (x1, y1, x2, y2, c1, k1, c2 = c1, k2 = k1, z = 0) => { // colours scaled by k
    if (n >= maxSegs) return;
    const o = n * 6;
    pos[o] = x1; pos[o + 1] = y1; pos[o + 2] = z; pos[o + 3] = x2; pos[o + 4] = y2; pos[o + 5] = z;
    cl[o] = c1.r * k1; cl[o + 1] = c1.g * k1; cl[o + 2] = c1.b * k1; cl[o + 3] = c2.r * k2; cl[o + 4] = c2.g * k2; cl[o + 5] = c2.b * k2;
    n++;
  };
  obj.end = () => {
    g.setDrawRange(0, n * 2);
    g.attributes.position.needsUpdate = true; g.attributes.color.needsUpdate = true;
  };
  return obj;
}

export const circlePts = (r, n = 96, a0 = 0, a1 = Math.PI * 2, cx = 0, cy = 0) =>
  Array.from({ length: n + 1 }, (_, i) => { const a = lerp(a0, a1, i / n); return [cx + Math.cos(a) * r, cy + Math.sin(a) * r]; });

export function rect(w, h, color = C.ink, opacity = 1) {
  const m = new THREE.Mesh(new THREE.PlaneGeometry(w, h),
    new THREE.MeshBasicMaterial({ color, transparent: true, opacity, depthTest: false, depthWrite: false }));
  m.frustumCulled = false;
  return m;
}

/** Point sprites sized in world px; per-point colour, size and alpha. soft 0 = crisp dot, 1 = gaussian glow. */
export function dots(n, { soft = 0, additive = false } = {}) {
  const g = new THREE.BufferGeometry();
  const P = new Float32Array(n * 3), Cc = new Float32Array(n * 3), S = new Float32Array(n), A = new Float32Array(n);
  g.setAttribute('position', new THREE.BufferAttribute(P, 3).setUsage(THREE.DynamicDrawUsage));
  g.setAttribute('aColor', new THREE.BufferAttribute(Cc, 3).setUsage(THREE.DynamicDrawUsage));
  g.setAttribute('aSize', new THREE.BufferAttribute(S, 1).setUsage(THREE.DynamicDrawUsage));
  g.setAttribute('aAlpha', new THREE.BufferAttribute(A, 1).setUsage(THREE.DynamicDrawUsage));
  const m = new THREE.ShaderMaterial({
    uniforms: { uScale: { value: DIST }, uSoft: { value: soft }, uPx: { value: PX } },
    vertexShader: `attribute vec3 aColor; attribute float aSize; attribute float aAlpha; uniform float uScale, uPx;
      varying vec3 vC; varying float vA; varying float vS;
      void main(){ vec4 mv = modelViewMatrix * vec4(position, 1.0); gl_Position = projectionMatrix * mv;
        float s = aSize * uScale / -mv.z; gl_PointSize = max(s, 1.5) * uPx; vA = aAlpha * min(1.0, s / 1.5); vC = aColor; vS = gl_PointSize; }`,
    fragmentShader: `uniform float uSoft; varying vec3 vC; varying float vA; varying float vS;
      void main(){ float r = length(gl_PointCoord - 0.5) * 2.0;
        float a = mix(1.0 - smoothstep(1.0 - 2.5 / vS, 1.0, r), exp(-r * r * 5.0) * step(r, 1.0), uSoft) * vA;
        if (a < 0.002) discard; gl_FragColor = vec4(vC, a); }`,
    transparent: true, depthTest: false, depthWrite: false,
    blending: additive ? THREE.AdditiveBlending : THREE.NormalBlending,
  });
  const p = new THREE.Points(g, m);
  p.frustumCulled = false;
  p.set = (i, x, y, z, c, size, alpha) => {
    P[i * 3] = x; P[i * 3 + 1] = y; P[i * 3 + 2] = z;
    Cc[i * 3] = c.r; Cc[i * 3 + 1] = c.g; Cc[i * 3 + 2] = c.b; S[i] = size; A[i] = alpha;
  };
  p.commit = () => { for (const k of ['position', 'aColor', 'aSize', 'aAlpha']) g.attributes[k].needsUpdate = true; };
  return p;
}

/** Text rendered to a canvas texture, tinted by material colour. `fit` reserves width for changing strings. */
export function text(str, o = {}) {
  const s = { size: 32, font: FONT.cond, weight: 700, color: C.ink, align: 'left', tracking: 0, opacity: 1, fit: str, ...o };
  const k = 2 * PX, cv = document.createElement('canvas'), g = cv.getContext('2d');
  const font = `${s.weight} ${s.size * k}px ${s.font}`;
  const style = () => { g.font = font; g.letterSpacing = `${s.tracking * k}px`; g.fontKerning = 'normal'; g.textRendering = 'geometricPrecision'; };
  style();
  cv.width = Math.ceil(g.measureText(String(s.fit)).width + 8 * k + Math.abs(s.tracking) * k);
  cv.height = Math.ceil(s.size * 1.3 * k);
  const tex = new THREE.CanvasTexture(cv);
  tex.minFilter = THREE.LinearMipmapLinearFilter; tex.anisotropy = 4;
  const mat = new THREE.MeshBasicMaterial({ map: tex, color: s.color, transparent: true, opacity: s.opacity, depthTest: false, depthWrite: false });
  const geo = new THREE.PlaneGeometry(cv.width / k, cv.height / k);
  geo.translate(s.align === 'left' ? cv.width / k / 2 - 4 : s.align === 'right' ? -cv.width / k / 2 + 4 : 0, 0, 0);
  const mesh = new THREE.Mesh(geo, mat);
  mesh.frustumCulled = false;
  let cur = null;
  mesh.set = (v) => {
    v = String(v);
    if (v === cur) return mesh;
    cur = v;
    g.clearRect(0, 0, cv.width, cv.height);
    style();
    g.fillStyle = '#fff'; g.textBaseline = 'middle';
    g.textAlign = s.align;
    const x = s.align === 'left' ? 4 * k : s.align === 'right' ? cv.width - 4 * k : cv.width / 2;
    const w = g.measureText(v).width, max = cv.width - 8 * k;
    mesh.textWidth = Math.min(w, max) / k; // drawn width in design px (before mesh scale)
    if (w > max) { g.save(); g.translate(x, 0); g.scale(max / w, 1); g.fillText(v, 0, cv.height / 2 + s.size * 0.06 * k); g.restore(); }
    else g.fillText(v, x, cv.height / 2 + s.size * 0.06 * k);
    tex.needsUpdate = true;
    return mesh;
  };
  mesh.typeOn = (k) => mesh.set(String(str).slice(0, Math.round(clamp(k) * String(str).length))); // typewriter reveal, k 0..1
  return mesh.set(str);
}

/** A plane showing a 2D canvas you redraw yourself: draw(g, w, h) in canvas px (2x supersampled). */
export function canvasPlane(w, h, draw) {
  const k = 2 * PX, cv = document.createElement('canvas');
  cv.width = w * k; cv.height = h * k;
  const g = cv.getContext('2d');
  const tex = new THREE.CanvasTexture(cv);
  tex.minFilter = THREE.LinearMipmapLinearFilter; tex.anisotropy = 4;
  const mesh = new THREE.Mesh(new THREE.PlaneGeometry(w, h),
    new THREE.MeshBasicMaterial({ map: tex, transparent: true, depthTest: false, depthWrite: false }));
  mesh.frustumCulled = false;
  mesh.redraw = (...args) => { g.setTransform(k, 0, 0, k, 0, 0); g.clearRect(0, 0, w, h); draw(g, w, h, ...args); tex.needsUpdate = true; return mesh; };
  return mesh.redraw();
}

/** A soft dark field from the top edge down to y = to (screen px), drawn under the HUD type so busy backgrounds recede behind it. */
export function scrim(to = 60, strength = 0.9) {
  const h = 360 - to, m = canvasPlane(W, h, (g, w, hh) => {
    const gr = g.createLinearGradient(0, 0, 0, hh);
    gr.addColorStop(0, `rgba(5,8,13,${strength})`); gr.addColorStop(0.55, `rgba(5,8,13,${strength * 0.75})`); gr.addColorStop(1, 'rgba(5,8,13,0)');
    g.fillStyle = gr; g.fillRect(0, 0, w, hh);
  });
  m.position.set(0, 360 - h / 2, 0);
  m.renderOrder = -1;
  return m;
}

// ---- motes -------------------------------------------------------------------------------------
/** Drifting specks in depth (soft, additive, faint) so the camera drift and push-in read as parallax. d.update(t) each frame. */
export function motes(n, { seed = 1, spread = [1900, 1100], z = [-700, 420], alpha = 0.08, color = C.ink, rise = 1 } = {}) {
  const R = rng(seed), d = dots(n, { soft: 1, additive: true }), c = col(color);
  const P = Array.from({ length: n }, () => ({ x: (R() - 0.5) * spread[0], y: R() * spread[1], z: lerp(z[0], z[1], R()),
    s: 2 + R() * 7, a: alpha * (0.25 + 0.75 * R()), v: (5 + R() * 12) * rise, ph: R() * 6.3 }));
  d.update = (t) => {
    P.forEach((p, k) => {
      const y = ((p.y + p.v * t) % spread[1]) - spread[1] / 2, edge = clamp((spread[1] / 2 - Math.abs(y)) / 120);
      d.set(k, p.x + 9 * Math.sin(t * 0.31 + p.ph), y, p.z, c, p.s, p.a * edge * (0.75 + 0.25 * Math.sin(t * 1.7 + p.ph * 3)));
    });
    d.commit();
  };
  return d;
}

// ---- time --------------------------------------------------------------------------------------
/** Story time <-> real time. A scene is written in story seconds (its default length D0). Rendered at length D, the stretches
 *  between beats scale evenly while the rigid windows (story seconds, around the beats) keep real speed, so a slam is equally
 *  fast at any length; window edges blend over 0.25 s so nothing jerks. The fade-up and the last 0.6 s are always rigid, so the fades
 *  cover the same story at any length. When the elastic parts would have to run faster than 1/FMIN or slower than 1/FMAX of their
 *  speed, the windows give way only as far as needed, down to an even scale. */
export function timeWarp(D0, D, rigid = []) {
  const N = 4096, ds = D0 / N, FMIN = 0.45, FMAX = 2;
  rigid = [[-1, 0.45], ...rigid, [D0 - 0.6, D0 + 1]];
  const hold = (s) => rigid.reduce((r, [a, b]) => Math.max(r, E.sine(seg(s, a - 0.25, a)) * (1 - E.sine(seg(s, b, b + 0.25)))), 0);
  const h = Float64Array.from({ length: N }, (_, k) => hold((k + 0.5) * ds));
  const H = h.reduce((a, b) => a + b, 0) * ds;  // story seconds held at real speed
  let r = 1, f = 1 + (D - D0) / Math.max(1e-6, D0 - H);
  const cap = f < FMIN ? FMIN : f > FMAX ? FMAX : 0;
  if (cap) { r = clamp((D0 - (D - D0) / (cap - 1)) / H); f = r > 0 ? cap : D / D0; }
  const w = h.map(v => 1 - r * v);                // 1 = elastic, 0 = real speed
  const T = new Float64Array(N + 1);
  for (let k = 0; k < N; k++) T[k + 1] = T[k] + ds * (1 + (f - 1) * w[k]);
  const real = (s) => { const x = clamp(s / ds, 0, N), k = Math.min(N - 1, Math.floor(x)); return lerp(T[k], T[k + 1], x - k); };
  const story = (t) => {
    if (t <= 0) return 0;
    if (t >= T[N]) return D0;
    let lo = 0, hi = N;
    while (hi - lo > 1) { const m = (lo + hi) >> 1; if (T[m] <= t) lo = m; else hi = m; }
    return (lo + (t - T[lo]) / (T[lo + 1] - T[lo])) * ds;
  };
  return { real, story, scale: f };
}

// ---- stage -------------------------------------------------------------------------------------
const VERT = `varying vec2 vUv; void main(){ vUv = uv; gl_Position = vec4(position.xy, 0.0, 1.0); }`;
const HASH = `float h12(vec2 p){ uvec2 q = uvec2(ivec2(p)) * uvec2(1597334673u, 3812015801u); uint n = (q.x ^ q.y) * 1597334673u; return float(n) / 4294967295.0; }`;

function fsMat(fragmentShader, uniforms) {
  return new THREE.ShaderMaterial({ vertexShader: VERT, fragmentShader, uniforms, depthTest: false, depthWrite: false });
}

/** mod.build(stage) returns update(s, i, t): s story seconds, i frame, t real seconds. It may return { glitch, shake, bloom, camera: { target } }.
 *  Beats: stage.beat(name, s) registers a story moment (exported to beats.json in real seconds); stage.since(s) is real seconds since
 *  that moment's frame (negative before), stage.at(s, n) is true on its first n frames. Use since() for reactions so they keep their speed. */
export function createStage(canvas, mod, D = mod.duration) {
  const renderer = new THREE.WebGLRenderer({ canvas, antialias: false, preserveDrawingBuffer: true, powerPreference: 'high-performance' });
  renderer.setPixelRatio(1);
  renderer.setSize(W * PX, H * PX, false);
  renderer.outputColorSpace = THREE.LinearSRGBColorSpace;
  renderer.autoClear = false;

  const scene = new THREE.Scene();
  const camera = new THREE.PerspectiveCamera(30, W / H, 5, 20000);
  const hud = new THREE.Scene();
  const hudCam = new THREE.OrthographicCamera(-W / 2, W / 2, H / 2, -H / 2, -100, 100);

  const rtOpts = { type: THREE.HalfFloatType, depthBuffer: false };
  const rt = new THREE.WebGLRenderTarget(W * PX, H * PX, { type: THREE.HalfFloatType, samples: 4 });
  const h1 = new THREE.WebGLRenderTarget(W * PX / 2, H * PX / 2, rtOpts), h2 = h1.clone();
  const q1 = new THREE.WebGLRenderTarget(W * PX / 4, H * PX / 4, rtOpts), q2 = q1.clone();

  const quadScene = new THREE.Scene(), quadCam = new THREE.OrthographicCamera(-1, 1, 1, -1, 0, 1);
  const quad = new THREE.Mesh(new THREE.PlaneGeometry(2, 2));
  quad.frustumCulled = false;
  quadScene.add(quad);
  const pass = (mat, target) => { quad.material = mat; renderer.setRenderTarget(target); renderer.clear(); renderer.render(quadScene, quadCam); };

  // bloom source: saturated (accent) colours and over-bright cores only, so the off-white ink stays crisp
  const extract = fsMat(`uniform sampler2D tex; varying vec2 vUv; void main(){
      vec3 c = texture2D(tex, vUv).rgb; float mx = max(c.r, max(c.g, c.b)), mn = min(c.r, min(c.g, c.b));
      float w = smoothstep(0.12, 0.45, mx - mn) * smoothstep(0.08, 0.4, mx) + 0.7 * smoothstep(1.0, 1.8, mx);
      gl_FragColor = vec4(c * w, 1.0); }`, { tex: { value: null } });
  const blur = fsMat(`uniform sampler2D tex; uniform vec2 dir; varying vec2 vUv; void main(){
      vec3 s = texture2D(tex, vUv).rgb * 0.227;
      s += (texture2D(tex, vUv + dir * 1.385).rgb + texture2D(tex, vUv - dir * 1.385).rgb) * 0.316;
      s += (texture2D(tex, vUv + dir * 3.231).rgb + texture2D(tex, vUv - dir * 3.231).rgb) * 0.070;
      gl_FragColor = vec4(s, 1.0); }`, { tex: { value: null }, dir: { value: new THREE.Vector2() } });
  const copy = fsMat(`uniform sampler2D tex; varying vec2 vUv; void main(){ gl_FragColor = texture2D(tex, vUv); }`, { tex: { value: null } });
  const post = fsMat(`${HASH}
    uniform sampler2D tScene, tA, tB; uniform float uFrame, uFade, uGlitch, uBloom, uGrain, uPx, uTime, uScan; uniform vec3 uBg; varying vec2 vUv;
    void main(){
      vec2 uv = vUv; float py = floor(gl_FragCoord.y / uPx);
      if (uGlitch > 0.0) {
        float band = floor(uv.y * 28.0 + h12(vec2(uFrame, 3.0)) * 9.0);
        if (h12(vec2(band, uFrame)) < 0.45 * uGlitch) uv.x += (h12(vec2(band, uFrame + 7.0)) - 0.5) * 0.10 * uGlitch;
        uv.x += (h12(vec2(floor(py / 2.0), uFrame + 11.0)) - 0.5) * 0.006 * uGlitch;
      }
      vec2 d = uv - 0.5; float e = dot(d, d);
      vec2 off = d * e * (0.018 + 0.05 * uGlitch);
      vec3 c = vec3(texture2D(tScene, uv + off).r, texture2D(tScene, uv).g, texture2D(tScene, uv - off).b);
      c += (texture2D(tA, uv).rgb * 0.8 + texture2D(tB, uv).rgb * 1.1) * uBloom;
      float m = max(c.r, max(c.g, c.b));                                               // over-bright accents keep their hue
      if (m > 1.0) c = mix(min(c, 1.0), c / m, 0.75);                                   // (a plain clip turns amber yellow)
      float sy = 1.15 - fract(uTime / 4.6) * 1.3, dy = vUv.y - sy;                       // slow scan band rolling down the frame
      c *= 1.0 + uScan * (0.05 * exp(-dy * dy * 900.0) + 0.05 * step(abs(dy + 0.012), 0.6 / 720.0));
      c *= mod(py, 3.0) < 1.0 ? 0.9 : 1.0;                                             // faint scanlines
      c *= mix(0.32, 1.0, smoothstep(0.95, 0.3, length(d * vec2(1.0, 0.82)) * 1.3));   // vignette
      float g = h12(gl_FragCoord.xy + vec2(mod(uFrame * 37.0, 1021.0), mod(uFrame * 91.0, 997.0))) - 0.5;
      c += g * uGrain * (0.55 + 0.45 * (1.0 - clamp(c.g, 0.0, 1.0)));                  // film grain, heavier in shadows
      c = mix(uBg * 0.5, c, uFade);
      gl_FragColor = vec4(c, 1.0);
    }`, {
    tScene: { value: rt.texture }, tA: { value: h1.texture }, tB: { value: q1.texture },
    uFrame: { value: 0 }, uFade: { value: 1 }, uGlitch: { value: 0 }, uBloom: { value: 1 }, uGrain: { value: 0.07 },
    uPx: { value: PX }, uTime: { value: 0 }, uScan: { value: mod.scan ?? 1 }, uBg: { value: col(C.bg) },
  });

  // HUD at the frame edges (nothing frames the picture): corner ticks, clip code and running time in the top band (decorative,
  // a wide screen may crop it), and a thin progress ruler between them.
  const ex = W / 2 - 18, ey = H / 2 - 18, arm = 12;
  for (const [x, y] of [[-1, 1], [1, 1], [-1, -1], [1, -1]])
    hud.add(line([[x * ex, y * (ey - arm)], [x * ex, y * ey], [x * (ex - arm), y * ey]], { opacity: 0.3 }));
  const tag = text(`${mod.code}  ${mod.title}`, { size: 11, font: FONT.mono, weight: 400, tracking: 3, opacity: 0.45 });
  tag.position.set(-ex + 22, ey - 5, 0);
  const clock = text('T+00:00.00', { size: 11, font: FONT.mono, weight: 400, tracking: 2, opacity: 0.45, align: 'right' });
  clock.position.set(ex - 22, ey - 5, 0);
  const ruler = segments(64, { additive: false }), ink = col(C.ink);
  hud.add(tag, clock, ruler);

  function rig(t, o) {
    const push = o.push ?? 0.07, tilt = o.tilt ?? 0, [dx, dy] = o.drift ?? [14, 8], roll = o.roll ?? 0.008;
    const [tx, ty] = o.target ?? [0, 0];
    const d = DIST * (1 - push * E.sine(seg(t, 0, D)));
    const x = tx + dx * Math.sin(t * 0.45 + 1.3), y = ty + dy * Math.sin(t * 0.37 + 0.2);
    camera.position.set(x, y - d * Math.sin(tilt), d * Math.cos(tilt));
    camera.up.set(0, 1, 0);
    camera.lookAt(x, y, 0);
    camera.rotateZ(roll * Math.sin(t * 0.5 + 0.4) + (o.spin ?? 0));
  }

  const warp = timeWarp(mod.duration, D, mod.rigid ?? []);
  const stage = { renderer, scene, camera, hud, update: null, dur: D, warp, beats: [], i: 0 };
  stage.beat = (name, s) => { stage.beats.push({ name, s }); return s; };
  stage.frameOf = (s) => Math.ceil(warp.real(s) * FPS - 1e-6);
  stage.since = (s) => (stage.i - stage.frameOf(s)) / FPS;
  stage.at = (s, n = 1) => { const k = stage.i - stage.frameOf(s); return k >= 0 && k < n; };

  stage.frame = (i) => {
    const t = i / FPS;
    stage.i = i;
    const fx = stage.update(warp.story(t), i, t) || {};
    rig(t, { ...mod.camera, ...fx.camera });
    if (fx.shake) {
      camera.position.x += (hash(i, 1) - 0.5) * fx.shake;
      camera.position.y += (hash(i, 2) - 0.5) * fx.shake;
    }
    const ss = Math.floor(t), cs = Math.floor((t - ss) * 100);
    clock.set(`T+00:${String(ss).padStart(2, '0')}.${String(cs).padStart(2, '0')}`);
    ruler.begin();
    for (let k = 0; k <= 48; k++) {
      const x = -144 + k * 6, on = k / 48 <= t / D;
      ruler.push(x, ey - 9, x, ey - (k % 8 === 0 ? 1 : 5), ink, on ? 0.42 : 0.12);
    }
    ruler.end();

    renderer.setRenderTarget(rt);
    renderer.setClearColor(C.bg, 1);
    renderer.clear();
    renderer.render(scene, camera);
    renderer.render(hud, hudCam);

    extract.uniforms.tex.value = rt.texture; pass(extract, h1);
    blur.uniforms.tex.value = h1.texture; blur.uniforms.dir.value.set(1 / (W / 2), 0); pass(blur, h2);
    blur.uniforms.tex.value = h2.texture; blur.uniforms.dir.value.set(0, 1 / (H / 2)); pass(blur, h1);
    copy.uniforms.tex.value = h1.texture; pass(copy, q1);
    for (let r = 1; r <= 2; r++) {
      blur.uniforms.tex.value = q1.texture; blur.uniforms.dir.value.set(r * 1.5 / (W / 4), 0); pass(blur, q2);
      blur.uniforms.tex.value = q2.texture; blur.uniforms.dir.value.set(0, r * 1.5 / (H / 4)); pass(blur, q1);
    }

    const u = post.uniforms;
    u.uFrame.value = i;
    u.uTime.value = t;
    u.uFade.value = E.out(seg(t, 0, 0.45)) * (1 - E.inOut(seg(t, D - 0.4, D - 1 / FPS)));
    u.uGlitch.value = fx.glitch ?? 0;
    u.uBloom.value = fx.bloom ?? mod.bloom ?? 1;
    pass(post, null);
  };
  return stage;
}
