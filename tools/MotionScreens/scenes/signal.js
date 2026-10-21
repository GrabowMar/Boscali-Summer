// 06 SIGNAL: the BRN test card counts down to 02:00:00, tears into noise and collapses like a dying tube on the stroke of two;
// a dot of afterglow, black, then a hard cut to the new owners: PROVISIONAL AUTHORITY / STAND BY.
import { THREE, C, FONT, E, PX, seg, lerp, hash, col, line, dots, text, segments, circlePts } from '../look.js';

const TEAR = 0.9, ROLL = 2.1, TCOL = 3.05, TOFF = 3.38, TCARD = 3.65; // story seconds

function drawCard(g, clock) {
  const w = 1280, h = 720;
  g.fillStyle = '#0a0e14'; g.fillRect(0, 0, w, h);
  g.strokeStyle = 'rgba(230,225,211,0.16)'; g.lineWidth = 1;
  for (let x = 0; x <= w; x += 64) { g.beginPath(); g.moveTo(x + 0.5, 0); g.lineTo(x + 0.5, h); g.stroke(); }
  for (let y = 40; y <= h; y += 64) { g.beginPath(); g.moveTo(0, y + 0.5); g.lineTo(w, y + 0.5); g.stroke(); }
  // castellations: the black/white border blocks of a broadcast test card, out to the frame edge
  for (let x = 0, k = 0; x < w; x += 32, k++) { g.fillStyle = k % 2 ? 'rgba(230,225,211,0.55)' : '#05080d'; g.fillRect(x, 0, 32, 18); g.fillRect(x, h - 18, 32, 18); }
  for (let y = 18, k = 0; y < h - 18; y += 32, k++) { g.fillStyle = k % 2 ? 'rgba(230,225,211,0.55)' : '#05080d'; g.fillRect(0, y, 18, 32); g.fillRect(w - 18, y, 18, 32); }
  g.strokeStyle = 'rgba(230,225,211,0.4)';
  for (const [cx, cy] of [[192, 168], [1088, 168], [192, 552], [1088, 552]]) {
    g.beginPath(); g.arc(cx, cy, 56, 0, Math.PI * 2); g.stroke();
    g.beginPath(); g.arc(cx, cy, 30, 0, Math.PI * 2); g.stroke();
    g.beginPath(); g.moveTo(cx - 70, cy); g.lineTo(cx + 70, cy); g.moveTo(cx, cy - 70); g.lineTo(cx, cy + 70); g.stroke();
  }
  // side gratings and the station ID block
  g.fillStyle = 'rgba(230,225,211,0.7)';
  for (const [x0, per] of [[64, 4], [1152, 6]]) for (let x = 0; x < 64; x += per) g.fillRect(x0 + x, 300, per / 2, 120);
  g.font = `400 13px ${FONT.mono}`; g.textAlign = 'left'; g.textBaseline = 'middle'; g.letterSpacing = '3px'; g.fillStyle = 'rgba(230,225,211,0.55)';
  g.fillText('BOSCALI REPUBLIC NATIONAL BROADCASTING', 64, 58); g.fillText('TX AGRAPOL MAST  /  CH 21  /  625', 64, 78);
  g.save();
  g.beginPath(); g.arc(640, 360, 250, 0, Math.PI * 2); g.fillStyle = '#05080d'; g.fill(); g.clip();
  g.fillStyle = C.amber; g.fillRect(380, 146, 520, 54);
  g.fillStyle = '#05080d'; g.font = `700 40px ${FONT.cond}`; g.textAlign = 'center'; g.textBaseline = 'middle'; g.letterSpacing = '10px';
  g.fillText('BRN 1', 645, 175);
  for (let k = 0; k < 8; k++) { const v = Math.round(lerp(226, 10, k / 7)); g.fillStyle = `rgb(${v},${v - 3},${v - 10})`; g.fillRect(380 + k * 65, 206, 65, 84); }
  g.fillStyle = '#e6e1d3';
  [12, 8, 6, 4, 3, 2].forEach((per, k) => { for (let x = 0; x < 82; x += per) g.fillRect(392 + k * 84 + x, 298, per / 2, 62); });
  g.fillStyle = '#05080d'; g.fillRect(470, 380, 340, 70);
  g.strokeStyle = 'rgba(230,225,211,0.6)'; g.strokeRect(470.5, 380.5, 340, 70);
  g.fillStyle = '#e6e1d3'; g.font = `700 44px ${FONT.mono}`; g.letterSpacing = '4px'; g.fillText(clock, 642, 417);
  g.font = `400 16px ${FONT.mono}`; g.letterSpacing = '8px'; g.fillStyle = 'rgba(230,225,211,0.75)'; g.fillText('SIGNAL  OK', 644, 488);
  g.restore();
  g.strokeStyle = 'rgba(230,225,211,0.85)'; g.lineWidth = 2; g.beginPath(); g.arc(640, 360, 250, 0, Math.PI * 2); g.stroke();
  g.lineWidth = 1; g.strokeStyle = 'rgba(230,225,211,0.55)';
  g.beginPath(); g.moveTo(640.5, 90); g.lineTo(640.5, 630); g.moveTo(370, 360.5); g.lineTo(910, 360.5); g.stroke();
  g.fillStyle = C.amber; g.beginPath(); g.roundRect(1000, 96, 104, 46, 6); g.fill();
  g.fillStyle = '#05080d'; g.font = `700 34px ${FONT.cond}`; g.letterSpacing = '4px'; g.textAlign = 'center'; g.textBaseline = 'middle'; g.fillText('BRN', 1054, 121);
}

const frag = `
  uniform sampler2D tCard; uniform float uT, uFrame, uTear, uNoise, uRoll, uCollapse, uPx; varying vec2 vUv;
  float h12(vec2 p){ uvec2 q = uvec2(ivec2(p)) * uvec2(1597334673u, 3812015801u); uint n = (q.x ^ q.y) * 1597334673u; return float(n) / 4294967295.0; }
  void main(){
    float cy = mix(1.0, 0.004, smoothstep(0.0, 0.55, uCollapse)), cx = mix(1.0, 0.003, smoothstep(0.5, 1.0, uCollapse));
    vec2 p = (vUv - 0.5) / vec2(cx, cy);
    if (abs(p.x) > 0.5 || abs(p.y) > 0.5) discard;
    vec2 uv = p + 0.5;
    float ft = floor(uT * 18.0), band = floor(uv.y * 16.0 + h12(vec2(ft, 5.0)) * 4.0);
    float big = step(1.0 - 0.4 * uTear, h12(vec2(band, ft)));
    uv.x += (h12(vec2(floor(uv.y * 240.0), ft)) - 0.5) * 0.012 * uTear + big * (h12(vec2(band, ft + 3.0)) - 0.5) * 0.35 * uTear;
    uv.x += sin(uv.y * 26.0 + uT * 23.0) * 0.008 * uTear;
    uv.y = fract(uv.y + uRoll);
    float sp = 0.002 + 0.016 * uTear;
    vec3 c = vec3(texture2D(tCard, uv + vec2(sp, 0.0)).r, texture2D(tCard, uv).g, texture2D(tCard, uv - vec2(sp, 0.0)).b);
    vec2 fc = floor(gl_FragCoord.xy / uPx);
    float n = h12(floor(fc / 2.0) + vec2(mod(uFrame * 53.0, 997.0), mod(uFrame * 71.0, 991.0)));
    c = mix(c, vec3(n * 0.85), uNoise);
    c *= 1.0 - 0.45 * uTear * step(2.0, mod(fc.y, 4.0));
    c += big * uTear * 0.08;
    float col = smoothstep(0.15, 0.9, uCollapse);
    c = c * (1.0 + 3.0 * col) + vec3(0.9, 0.88, 0.84) * col;
    gl_FragColor = vec4(c, 1.0);
  }`;

export default {
  code: '06', title: 'SIGNAL', duration: 5.5, bloom: 0.4,
  rigid: [[TCOL - 0.25, TCARD + 0.45]],
  camera: { push: 0.07, drift: [8, 5], roll: 0.004 },
  build(S) {
    S.beat('tear', TEAR);
    S.beat('roll', ROLL);
    S.beat('collapse', TCOL);
    S.beat('off', TOFF);
    S.beat('card', TCARD);
    const cv = document.createElement('canvas');
    cv.width = 1280 * 2 * PX; cv.height = 720 * 2 * PX;
    const g = cv.getContext('2d');
    g.scale(2 * PX, 2 * PX);
    const tex = new THREE.CanvasTexture(cv);
    tex.minFilter = THREE.LinearMipmapLinearFilter;
    let shownClock = '';
    const card = new THREE.Mesh(new THREE.PlaneGeometry(1280, 720), new THREE.ShaderMaterial({
      uniforms: { tCard: { value: tex }, uT: { value: 0 }, uFrame: { value: 0 }, uTear: { value: 0 }, uNoise: { value: 0 }, uRoll: { value: 0 }, uCollapse: { value: 0 }, uPx: { value: PX } },
      vertexShader: 'varying vec2 vUv; void main(){ vUv = uv; gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0); }',
      fragmentShader: frag, depthTest: false, depthWrite: false,
    }));
    card.frustumCulled = false;
    card.scale.setScalar(1.05); // overfills the frame through the drift
    const dot = dots(1, { soft: 1, additive: true });
    const red = col(C.red), white = col(C.ink).multiplyScalar(2);

    // the new owners' card: a slow ring emblem behind, faint red raster edge to edge, then the type
    const emblem = new THREE.Group();
    emblem.position.set(0, 10, -260);
    const er = segments(200, { additive: false });
    er.begin();
    for (let k = 0; k < 180; k++) { const a = k / 180 * Math.PI * 2, r0 = k % 15 === 0 ? 372 : 386; er.push(Math.cos(a) * r0, Math.sin(a) * r0, Math.cos(a) * 396, Math.sin(a) * 396, red, k % 15 === 0 ? 1 : 0.5); }
    er.end();
    emblem.add(er, line(circlePts(410, 200), { color: C.red, opacity: 0.5 }), line(circlePts(300, 200), { color: C.red, opacity: 0.3, dashed: true, dash: 4, gap: 8 }));
    const raster = segments(140, { additive: false });
    raster.begin(); for (let y = -380; y <= 380; y += 6) raster.push(-760, y, 760, y, red, 1); raster.end();
    raster.position.z = -40;
    const head = text('BRN 1  /  ALL TRANSMITTERS  /  02:00', { size: 13, font: FONT.mono, weight: 400, tracking: 5, color: C.red, align: 'center' });
    head.position.set(0, 128, 0);
    const title = text('PROVISIONAL AUTHORITY', { size: 100, font: FONT.cond, weight: 700, tracking: 2, color: C.red, align: 'center' });
    title.position.set(0, 46, 0);
    const rule = line([[-320, -12], [320, -12]], { color: C.red, opacity: 0.8 });
    const stand = text('STAND BY', { size: 30, font: FONT.mono, weight: 700, tracking: 18, color: C.red, align: 'center' });
    stand.position.set(9, -52, 0);
    const notice = text('THIS STATION IS UNDER THE PROTECTION OF THE ARMED FORCES', { size: 12, font: FONT.mono, weight: 400, tracking: 3, color: C.red });
    notice.position.set(-notice.textWidth / 2, -112, 0);
    const after = [emblem, raster, head, title, rule, stand, notice];
    S.scene.add(card, dot, ...after);

    return (s, i, t) => {
      // the card clock counts real seconds to 02:00:00, which falls on the collapse
      const since = S.since(TCOL), sec = Math.floor(since);
      const clock = since < 0 ? `01:59:${String(60 + sec).padStart(2, '0')}` : `02:00:${String(sec).padStart(2, '0')}`;
      if (clock !== shownClock) { drawCard(g, clock); tex.needsUpdate = true; shownClock = clock; }
      const u = card.material.uniforms, jit = hash(Math.floor(t * 12), 4);
      u.uT.value = t; u.uFrame.value = i;
      u.uTear.value = Math.min(1, 0.025 + (S.at(TEAR, 2) ? 0.45 : 0) + E.in(seg(s, 1.3, 3.0)) * (0.55 + 0.45 * jit));
      u.uNoise.value = Math.min(0.95, 0.78 * E.in(seg(s, 1.7, TCOL)) * (0.45 + 0.55 * hash(i, 8)) + (S.at(2.4) ? 0.6 : 0));
      u.uRoll.value = s < ROLL ? 0 : s < 2.45 ? 0.13 : 0.13 + (s - 2.45) * 0.8;
      u.uCollapse.value = E.in(seg(s, TCOL, TOFF));
      card.visible = s < TOFF;

      // afterglow: the dot lingers and fades in real time
      const off = S.since(TOFF - 0.03), dk = seg(off, 0, 0.3);
      dot.set(0, 0, 0, 0, white, 26 * (1 - dk) + 6, off >= 0 && s < TCARD ? Math.exp(-off * 7) : 0);
      dot.commit();

      const on = s >= TCARD, k = S.since(TCARD), flick = 0.9 + 0.1 * hash(i, 2);
      for (const o of after) o.visible = on;
      title.material.opacity = flick;
      title.material.color.copy(red).multiplyScalar(on ? 1 + 0.8 * Math.exp(-k * 6) : 1);
      head.material.opacity = 0.7 * flick;
      stand.material.opacity = flick * (Math.floor(Math.max(0, k) * 1.6) % 2 === 0 ? 1 : 0.5);
      notice.typeOn(seg(k, 0.3, 0.9)); notice.material.opacity = 0.65;
      raster.material.opacity = 0.045 * flick;
      emblem.rotation.z = -0.05 * t;
      er.material.opacity = 0.2;

      const glitch = S.at(TEAR) ? 0.4 : S.at(2.4) ? 0.6 : S.at(2.8) ? 0.8 : S.at(TCARD) ? 1 : 0;
      return { glitch, bloom: on ? 0.85 : 0.4 };
    };
  },
};
