// 05 RIOTS: a drone feed over the harbour road. A crowd of dots surges against a thin shield line in three waves; flares arc over and
// burn in drifting smoke; ports report riots one by one; the line bends and breaks (HOLDING -> BROKEN) and the police pull back.
import { THREE, C, FONT, E, seg, lerp, clamp, rng, hash, col, line, segments, dots, text, rect, motes, scrim } from '../look.js';

const LY = -70, XB = 40, NC = 5200;
const HITS = [[1.75, 10], [2.85, 16], [3.9, 26]];      // story seconds a wave reaches the line, bend it adds
const TB = 4.3;                                         // the line breaks
const pulse = (t, c, w = 0.35) => Math.exp(-(((t - c) / w) ** 2));
const bend = (s) => HITS.reduce((a, [h, b]) => a + b * E.out(seg(s, h - 0.08, h + 0.4)), 0) + 40 * E.out(seg(s, TB - 0.1, TB + 1.2));
const lineY = (x, s) => LY - bend(s) * Math.exp(-(((x - XB) / 170) ** 2));
const FLARES = [ // throw time, from, to, flight seconds
  [0.7, [-150, 170], [-90, -165], 1.0], [1.9, [130, 230], [70, -190], 1.1], [3.0, [-260, 120], [-300, -215], 0.95], [3.9, [300, 90], [230, 40], 0.8],
];
const PORTS = [['AGRAPOL HARBOUR', 0.9], ['FELDSPAR', 1.6], ['OPAL STATION', 2.3], ['VARGA', 3.0], ['KESTREL BAY', 3.6]];
const GROUPS = [['GRP 04', -260, 150, 2140], ['GRP 07', 70, 70, 3810], ['GRP 11', 310, 210, 1260]]; // label, x0, depth, head count

const smokeFrag = `
  uniform float uT; uniform vec4 uSrc[4]; uniform vec2 uWind; varying vec2 vP;
  float h(vec2 p){ return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
  float vn(vec2 p){ vec2 i = floor(p), f = fract(p); f = f * f * (3.0 - 2.0 * f);
    return mix(mix(h(i), h(i + vec2(1, 0)), f.x), mix(h(i + vec2(0, 1)), h(i + vec2(1, 1)), f.x), f.y); }
  float fbm(vec2 p){ float s = 0.0, a = 0.5; for (int k = 0; k < 5; k++) { s += a * vn(p); p = p * 2.03 + 17.1; a *= 0.5; } return s; }
  void main(){
    vec2 w = normalize(uWind), n = vec2(-w.y, w.x);
    float dens = 0.10, glow = 0.0;
    for (int k = 0; k < 4; k++) {
      float age = uSrc[k].z; if (age < 0.0) continue;
      vec2 q = vP - uSrc[k].xy; float along = dot(q, w), across = dot(q, n);
      float sig = 18.0 + max(along, 0.0) * 0.45;
      dens += exp(-across * across / (2.0 * sig * sig)) * smoothstep(-30.0, 25.0, along) * exp(-max(along, 0.0) / (50.0 + age * 110.0)) * clamp(age * 1.5, 0.0, 1.0) * 1.3;
      glow += exp(-dot(q, q) / (2.0 * 70.0 * 70.0)) * uSrc[k].w;
    }
    float f = fbm(vP * 0.006 - uWind * uT * 0.006);
    float a = clamp(dens * smoothstep(0.35, 0.85, f) * 0.85, 0.0, 0.6);
    vec3 c = mix(vec3(0.62, 0.61, 0.58), vec3(0.88, 0.27, 0.23), clamp(glow, 0.0, 1.0) * 0.5);
    gl_FragColor = vec4(c, a + glow * 0.04);
  }`;

export default {
  code: '05', title: 'RIOTS', duration: 7.5, bloom: 1.25,
  rigid: [[TB - 0.3, TB + 0.9]],
  camera: { push: 0.13, tilt: 0.3, drift: [10, 6] },
  build(S) {
    HITS.forEach(([h], k) => S.beat(`wave-${k + 1}`, h));
    FLARES.forEach(([t0, , , dur], k) => S.beat(`flare-${k + 1}`, t0 + dur));
    S.beat('break', TB);
    const R = rng(505), ink = col(C.ink), red = col(C.red), hot = col(C.red).multiplyScalar(2.2);
    const world = new THREE.Group();
    S.scene.add(world);

    // the street: building blocks either side with height (roof, corners), kerb lines, a far road grid
    for (const sx of [-1, 1]) {
      for (let b = 0; b < 6; b++) {
        const y0 = -560 + b * 190, h = 160 + R() * 20, x0 = sx * 475, x1 = sx * 780, z = 40 + R() * 70;
        const box = (ins, op, zz = 0) => line([[x0 + sx * ins, y0 + ins, zz], [x1, y0 + ins, zz], [x1, y0 + h - ins, zz], [x0 + sx * ins, y0 + h - ins, zz]], { closed: true, opacity: op });
        world.add(box(0, 0.22), box(12, 0.07), box(0, 0.3, z));
        for (const [cx, cy] of [[x0, y0], [x0, y0 + h]]) world.add(line([[cx, cy, 0], [cx, cy, z]], { opacity: 0.14 }));
      }
      world.add(line([[sx * 462, -700], [sx * 462, 700]], { opacity: 0.12 }), line([[sx * 300, -700], [sx * 300, 700]], { dashed: true, dash: 10, gap: 14, opacity: 0.05 }));
    }

    // crowd
    const crowd = dots(NC), P = [];
    for (let i = 0; i < NC; i++) {
      const x0 = (R() * 2 - 1) * 448, d = 6 + Math.pow(R(), 2.1) * 370;
      const breach = d < 220 && R() < Math.exp(-(((x0 - XB) / 125) ** 2)) * 1.1;
      P.push({ x0, d, f1: 0.6 + R() * 1.6, f2: 0.5 + R() * 1.4, p1: R() * 6.3, p2: R() * 6.3, a: 0.45 + R() * 0.45, s: 2.6 + R() * 1.4,
        breach, sb: TB + 0.05 + d / 260 + R() * 0.15, v: 110 + R() * 90, spread: 0.6 + R() * 0.9 });
    }
    // shields and the police rows behind them
    const NS = 34, shieldX = Array.from({ length: NS }, (_, k) => -429 + k * 26);
    const shields = new THREE.InstancedMesh(new THREE.PlaneGeometry(20, 4.5), new THREE.MeshBasicMaterial({ color: 0xffffff, transparent: true, depthTest: false, depthWrite: false }), NS);
    shields.frustumCulled = false;
    const SH = shieldX.map(x => ({ x, dx: (x - XB) / Math.abs(x - XB || 1) * (60 + R() * 110), dy: -(18 + R() * 40), rot: (R() - 0.5) * 2.8, hit: Math.abs(x - XB) < 105 }));
    const police = dots(NS * 3);
    const PO = [];
    for (let r = 0; r < 3; r++) for (const x of shieldX) PO.push({ x: x + (r % 2) * 13, y: LY - 22 - r * 20, back: 80 + R() * 160, side: (R() - 0.5) * 60 });

    // smoke and flares
    const smoke = new THREE.Mesh(new THREE.PlaneGeometry(1800, 1300), new THREE.ShaderMaterial({
      uniforms: { uT: { value: 0 }, uSrc: { value: [0, 1, 2, 3].map(() => new THREE.Vector4(0, 0, -1, 0)) }, uWind: { value: new THREE.Vector2(26, 15) } },
      vertexShader: 'varying vec2 vP; void main(){ vec4 w = modelMatrix * vec4(position, 1.0); vP = w.xy; gl_Position = projectionMatrix * viewMatrix * w; }',
      fragmentShader: smokeFrag, transparent: true, depthTest: false, depthWrite: false,
    }));
    smoke.frustumCulled = false;
    const trails = segments(4 * 16, { additive: true });
    const fl = dots(4, { additive: true }), flGlow = dots(4, { soft: 1, additive: true });
    const shock = line(Array.from({ length: 97 }, (_, i) => [Math.cos(i / 96 * Math.PI * 2), Math.sin(i / 96 * Math.PI * 2) * 0.6]), { color: C.red });
    const brackets = segments(GROUPS.length * 8, { additive: true });
    const gLabs = GROUPS.map(([name]) => text(name, { size: 10, font: FONT.mono, weight: 700, tracking: 2, fit: `${name}  0 000` }));
    world.add(police, shields, crowd, trails, smoke, flGlow, fl, shock, brackets, ...gLabs);
    [police, shields, crowd, trails, smoke, flGlow, fl, shock, brackets].forEach((o, k) => { o.renderOrder = 10 + k; });
    gLabs.forEach(m => { m.renderOrder = 30; });
    const dust = motes(30, { seed: 51, alpha: 0.05 });
    S.scene.add(dust);

    // type: crowd estimate + feed top left; line status + ports top right
    const est = text('CROWD EST  00 000', { size: 14, font: FONT.mono, weight: 700, tracking: 4, opacity: 0.8, fit: 'CROWD EST  00 000' });
    est.position.set(-560, 258, 0);
    const feed = text('UAV-2  /  AGRAPOL HARBOUR ROAD  /  ALT 1 240 M', { size: 11, font: FONT.mono, weight: 400, tracking: 2, opacity: 0.5 });
    feed.position.set(-560, 238, 0);
    const stLab = text('POLICE LINE', { size: 12, font: FONT.mono, weight: 400, tracking: 4, opacity: 0.6, align: 'right' });
    stLab.position.set(560, 258, 0);
    const status = text('HOLDING', { size: 64, font: FONT.cond, weight: 700, align: 'right', fit: 'HOLDING', tracking: -0.5 });
    status.position.set(564, 214, 0); status.renderOrder = 2;
    const stBox = rect(1, 62, C.red, 0); stBox.renderOrder = 1;
    const withdrawn = text('UNITS WITHDRAWING', { size: 12, font: FONT.mono, weight: 700, tracking: 3, color: C.red, align: 'right' });
    withdrawn.position.set(560, 168, 0);
    const portHead = text('PORTS REPORTING UNREST', { size: 11, font: FONT.mono, weight: 400, tracking: 2, opacity: 0.5 });
    portHead.position.set(-560, 298, 0);
    const ports = PORTS.map(([name, at], k) => {
      const x = -340 + k * 180;
      const v = rect(7, 7, C.red); v.position.set(x + 3, 298, 0);
      const n = text(name, { size: 11, font: FONT.mono, weight: 400, tracking: 2, opacity: 0.6 }); n.position.set(x + 12, 298, 0);
      S.hud.add(n, v); return { n, v, at };
    });
    S.hud.add(scrim(30, 0.85), est, feed, stLab, stBox, status, withdrawn, portHead);

    const m4 = new THREE.Matrix4(), q = new THREE.Quaternion(), zAxis = new THREE.Vector3(0, 0, 1), v3 = new THREE.Vector3(), one = new THREE.Vector3(1, 1, 1), sc = new THREE.Color();
    const flarePos = (f, s) => { // [x, y, height] while flying, landed after
      const [t0, a, b, dur] = f, u = clamp((s - t0) / dur);
      return [lerp(a[0], b[0], u), lerp(a[1], b[1], u), 4 * 150 * u * (1 - u)];
    };
    const comp = (s) => 1 - 0.12 * seg(s, 0, TB) - HITS.reduce((a, [h], k) => a + (0.1 + 0.07 * k) * pulse(s, h - 0.2), 0);
    const crowdAt = (x0, d, s) => [x0, lineY(x0, s) + 7 + d * comp(s) - HITS.reduce((a, [h, b]) => a + b * 0.9 * pulse(s - h + d / 600, 0, 0.3), 0) * Math.exp(-d / 160)];

    return (s, i, t) => {
      dust.update(t);
      const cp = comp(s), sinceB = S.since(TB), broken = sinceB >= 0;
      for (let n = 0; n < NC; n++) {
        const p = P[n];
        const wave = HITS.reduce((a, [h, b]) => a + b * 0.9 * pulse(s - h + p.d / 600, 0, 0.3), 0);
        let x = p.x0 + 2.5 * Math.sin(t * p.f1 * 3 + p.p1);
        let y = lineY(p.x0, s) + 7 + p.d * cp - wave * Math.exp(-p.d / 160) + 2.5 * Math.sin(t * p.f2 * 3 + p.p2);
        if (s > TB) { // the gap pulls the near crowd in
          const near = Math.exp(-(((p.x0 - XB) / 220) ** 2));
          x = lerp(x, XB + (p.x0 - XB) * 0.7, near * 0.35 * E.inOut(seg(s, TB, TB + 2)));
          y -= p.d * 0.45 * near * E.inOut(seg(s, TB, TB + 2.2));
        }
        if (p.breach && s > p.sb) {
          const u = s - p.sb;
          x = lerp(x, XB + (p.x0 - XB) * 0.35, E.inOut(clamp(u * 1.8))) + (p.x0 - XB) * p.spread * Math.max(0, u - 0.4);
          y -= p.v * u * (1 + 0.3 * u);
        }
        crowd.set(n, x, y, 0, ink, p.s, p.a);
      }
      crowd.commit();

      // shields: grey, flash red as a wave lands and when the line goes, then the middle ones scatter
      const hitFlash = HITS.reduce((a, [h]) => { const k = S.since(h); return a + (k >= 0 ? Math.exp(-k * 8) : 0); }, 0);
      for (const [k, sh] of SH.entries()) {
        const bk = sh.hit ? E.out(seg(s, TB, TB + 0.9)) : 0, wob = Math.sin(t * 9 + k) * 0.6 * seg(s, 1, TB);
        q.setFromAxisAngle(zAxis, sh.rot * bk + (sh.hit ? 0 : wob * 0.02));
        v3.set(sh.x + sh.dx * bk, lineY(sh.x, s) + sh.dy * bk, 0);
        shields.setMatrixAt(k, m4.compose(v3, q, one));
        const near = Math.exp(-(((sh.x - XB) / 200) ** 2));
        shields.setColorAt(k, sc.copy(ink).lerp(red, Math.min(1, near * (0.6 * hitFlash + (broken ? 1.2 * Math.exp(-sinceB * 2.5) : 0)))));
      }
      shields.instanceMatrix.needsUpdate = true;
      if (shields.instanceColor) shields.instanceColor.needsUpdate = true;
      PO.forEach((o, k) => { // after the break the police rows fall back and thin out
        const near = Math.exp(-(((o.x - XB) / 260) ** 2)), rk = E.out(seg(s, TB + 0.15, TB + 2.2)) * (0.35 + 0.65 * near);
        police.set(k, o.x + o.side * rk, o.y - (bend(s) * Math.exp(-(((o.x - XB) / 170) ** 2))) - o.back * rk, 0, ink, 3.6, 0.45 * (1 - 0.5 * rk));
      });
      police.commit();

      // flares: arcs with trails, then burning on the ground
      trails.begin();
      FLARES.forEach((f, k) => {
        const [t0, , , dur] = f, live = s >= t0;
        const [x, y, z] = flarePos(f, s), flying = live && s < t0 + dur, age = s - t0 - dur;
        const flick = 0.75 + 0.25 * hash(i, k), land = S.since(t0 + dur);
        fl.set(k, x, y, z, hot, flying ? 7 : 6, live ? flick : 0);
        flGlow.set(k, x, y, z, red, flying ? 34 : 58 + 10 * hash(i, k + 9) + (land >= 0 ? 60 * Math.exp(-land * 7) : 0), live ? (flying ? 0.45 : 0.22 * flick + (land >= 0 ? 0.5 * Math.exp(-land * 6) : 0)) : 0);
        if (flying) for (let j = 0; j < 14; j++) {
          const a = flarePos(f, Math.max(t0, s - j * 0.03)), b = flarePos(f, Math.max(t0, s - (j + 1) * 0.03));
          trails.push(a[0], a[1], b[0], b[1], red, 1.2 * (1 - j / 14), red, 1.2 * (1 - (j + 1) / 14), a[2]);
        }
        smoke.material.uniforms.uSrc.value[k].set(f[2][0], f[2][1], live && age > 0 ? age : -1, live && age > 0 ? flick : 0);
      });
      trails.end(); fl.commit(); flGlow.commit();
      smoke.material.uniforms.uT.value = t;

      // the break: a shock ring at the gap
      const rk = seg(sinceB, 0, 1.0);
      shock.position.set(XB, lineY(XB, s), 0);
      shock.scale.setScalar(10 + 260 * E.outExpo(rk));
      shock.material.opacity = broken ? 0.9 * (1 - rk) : 0;

      // tracking brackets on three groups in the crowd
      brackets.begin();
      GROUPS.forEach(([name, x0, d, count], k) => {
        const [gx, gy] = crowdAt(x0, d, s), on = E.out(seg(s, 0.6 + k * 0.25, 0.9 + k * 0.25)) * (hash(Math.floor(t * 12), k) < 0.08 ? 0.3 : 1);
        const lost = broken && sinceB > 0.2 + k * 0.15, c = lost ? red : ink, a = on * (lost ? 0.9 : 0.6), w = 30, hgt = 22, arm = 8;
        for (const [sx, sy] of [[-1, -1], [1, -1], [-1, 1], [1, 1]]) {
          brackets.push(gx + sx * w, gy + sy * hgt, gx + sx * (w - arm), gy + sy * hgt, c, a);
          brackets.push(gx + sx * w, gy + sy * hgt, gx + sx * w, gy + sy * (hgt - arm), c, a);
        }
        gLabs[k].set(lost ? `${name}  LOST` : `${name}  ${String(Math.round(count * (1 + 0.04 * Math.sin(t * 2 + k)))).replace(/(\d)(\d{3})$/, '$1 $2')}`);
        gLabs[k].position.set(gx - w, gy + hgt + 10, 0);
        gLabs[k].material.color.copy(c); gLabs[k].material.opacity = a;
      });
      brackets.end();

      // type
      const intro = E.out(seg(s, 0.3, 0.9));
      est.set(`CROWD EST  ${String(Math.round(lerp(31000, 38000, E.out(seg(s, 0.5, 4))) / 10) * 10).replace(/(\d)(\d{3})$/, '$1 $2')}`);
      est.material.opacity = 0.8 * intro;
      feed.typeOn(seg(s, 0.4, 1.2));
      status.set(broken ? 'BROKEN' : 'HOLDING');
      status.material.color.copy(broken ? col(C.bg) : ink).lerp(red, broken ? 0 : 0.6 * hitFlash);
      status.scale.setScalar(broken ? lerp(1.25, 1, E.outExpo(seg(sinceB, 0, 0.35))) : 1 + 0.03 * hitFlash);
      status.material.opacity = stLab.material.opacity = intro;
      stBox.visible = broken;
      stBox.scale.set(status.textWidth * status.scale.x + 26, status.scale.y, 1);
      stBox.position.set(564 - status.textWidth * status.scale.x / 2 - 4, 216, 0);
      stBox.material.opacity = E.out(seg(sinceB, 0, 0.1)) * (0.9 + 0.1 * (Math.floor(t * 3) % 2));
      withdrawn.visible = sinceB > 0.5; withdrawn.typeOn(seg(sinceB, 0.5, 1.1));
      withdrawn.material.opacity = Math.floor(t * 2.5) % 2 ? 0.55 : 1;
      portHead.material.opacity = 0.5 * E.out(seg(s, 0.6, 1.0));
      for (const pt of ports) {
        pt.n.material.opacity = 0.6 * E.out(seg(s, 0.6, 1.0));
        const k = S.since(pt.at);
        pt.v.visible = k >= 0;
        pt.v.material.color.copy(red).multiplyScalar(k >= 0 ? 1 + 1.2 * Math.exp(-k * 6) : 1);
      }

      const glitch = S.at(TB, 2) ? 1 : HITS.some(([h]) => S.at(h)) ? 0.3 : 0;
      return {
        glitch, shake: (broken ? 18 * Math.exp(-sinceB * 6) : 0) + 5 * hitFlash,
        camera: { target: [lerp(0, XB * 0.6, E.inOut(seg(s, 1, 6))), lerp(56, -10, E.inOut(seg(s, 0, 7.5)))] },
      };
    };
  },
};
