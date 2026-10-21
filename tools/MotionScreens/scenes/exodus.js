// 07 EXODUS: the Republic Navy streams out through the breakwater, across the dashed 12 NM line into the dark. Shore guns reach some of
// them: tracers, a flash, and they burn where they stop, most of them at the breakwater. CROSSED counts the ones that make it.
import { THREE, C, FONT, E, seg, lerp, clamp, rng, hash, noise1, col, line, segments, dots, text, circlePts, motes, scrim } from '../look.js';

const NM = 140, NSHIP = 24, MOUTH = [0, -176];
const LEFT = [[-1000, -40], [-760, -70], [-520, -112], [-330, -150], [-180, -162], [-90, -172], [-58, -192], [-92, -238], [-210, -302], [-300, -420], [-330, -620]];
const RIGHT = [[1000, 20], [780, -12], [560, -72], [360, -122], [200, -150], [92, -166], [56, -188], [96, -242], [222, -322], [312, -442], [340, -620]];
const HITS = { 5: 40, 7: -168, 10: 110, 14: -186, 17: -160 }; // ship index -> y where it is hit (three of them at the breakwater)
const GUNS = [[-300, -250], [290, -262], [-520, -150]];          // shore batteries the tracers come from

function coast(ctrl, seed, side) {
  const curve = new THREE.CatmullRomCurve3(ctrl.map(([x, y]) => new THREE.Vector3(x, y, 0)));
  const pts = curve.getSpacedPoints(260);
  return (off) => pts.map((p, k) => {
    const tg = curve.getTangentAt(k / 260), j = 7 * (noise1(k * 0.15, seed) - 0.5) + 3 * (noise1(k * 0.6, seed + 1) - 0.5);
    const nx = -tg.y * side, ny = tg.x * side;
    return [p.x + nx * (off + j), p.y + ny * (off + j)];
  });
}

export default {
  code: '07', title: 'EXODUS', duration: 6.5, bloom: 1.25,
  camera: { push: 0.1, tilt: 0.62, drift: [12, 6] },
  build(S) {
    const R = rng(707), ink = col(C.ink), blue = col(C.blue), flashCol = col(C.ink).multiplyScalar(2.4), fire = col('#f0d9b0');
    const world = new THREE.Group();
    world.scale.setScalar(0.8);
    S.scene.add(world);

    // chart grid that recedes into the dark, range rings from the harbour mouth
    const grid = segments(80, { additive: false });
    grid.begin();
    for (let x = -1200; x <= 1200; x += 100) grid.push(x, -650, x, 1100, ink, 1);
    for (let y = -600; y <= 1100; y += 100) grid.push(-1200, y, 1200, y, ink, 1);
    grid.end(); grid.material.opacity = 0.05;
    world.add(grid);
    for (const [r, lab] of [[173, '6 NM'], [520, '18 NM']]) {
      const arc = line(circlePts(r, 160, 0.12 * Math.PI, 0.88 * Math.PI, ...MOUTH), { dashed: true, dash: 3, gap: 7, opacity: 0.12 });
      const m = text(lab, { size: 11, font: FONT.mono, weight: 400, tracking: 3, opacity: 0.35 });
      m.position.set(MOUTH[0] + Math.cos(0.8 * Math.PI) * r - 8, MOUTH[1] + Math.sin(0.8 * Math.PI) * r + 10, 0);
      world.add(arc, m);
    }

    // land fill + coast + seaward contours
    for (const [ctrl, seed, side] of [[LEFT, 1, 1], [RIGHT, 2, -1]]) {
      const c = coast(ctrl, seed, side), edge = c(0);
      const shape = new THREE.Shape(edge.map(([x, y]) => new THREE.Vector2(x, y)));
      shape.lineTo(side * -1000, -650); shape.lineTo(edge[0][0], -650);
      const fillM = new THREE.Mesh(new THREE.ShapeGeometry(shape), new THREE.MeshBasicMaterial({ color: C.land, depthTest: false, depthWrite: false }));
      world.add(fillM, line(edge, { opacity: 0.6 }), line(c(12), { opacity: 0.2 }), line(c(28), { opacity: 0.09 }), line(c(-16), { opacity: 0.07 }));
    }
    // the breakwater: two arms closing the harbour mouth to one gap
    const arms = [[[-58, -192], [-24, -146]], [[56, -188], [22, -144]]];
    for (const a of arms) world.add(line(a, { width: 2.4, opacity: 0.75 }));
    const bwLab = text('BREAKWATER', { size: 10, font: FONT.mono, weight: 400, tracking: 3, opacity: 0.45 });
    bwLab.position.set(64, -146, 0);
    const lights = dots(2, { soft: 1, additive: true });
    const guns = dots(GUNS.length, { soft: 1, additive: true });
    world.add(bwLab, lights, guns);

    // 12 NM line, and a curtain of light standing on it (the edge of the Republic)
    const nm = line([[-1200, NM], [1200, NM]], { color: C.blue, dashed: true, dash: 10, gap: 8, opacity: 0.55 });
    const nmLab = text('12 NM  /  TERRITORIAL LIMIT', { size: 15, font: FONT.mono, weight: 700, tracking: 4, color: C.blue });
    nmLab.position.set(-470, NM + 16, 0);
    const curtain = new THREE.Mesh(new THREE.PlaneGeometry(2400, 90), new THREE.ShaderMaterial({
      uniforms: { uColor: { value: blue }, uGlow: { value: 0.3 } },
      vertexShader: 'varying vec2 vUv; void main(){ vUv = uv; gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0); }',
      fragmentShader: 'uniform vec3 uColor; uniform float uGlow; varying vec2 vUv; void main(){ float a = pow(clamp(1.0 - vUv.y, 0.0, 1.0), 2.2) * uGlow * smoothstep(0.0, 0.25, min(vUv.x, 1.0 - vUv.x)); gl_FragColor = vec4(uColor * a, 1.0); }',
      transparent: true, depthTest: false, depthWrite: false, blending: THREE.AdditiveBlending,
    }));
    curtain.rotation.x = Math.PI / 2; curtain.position.set(0, NM, 45); curtain.frustumCulled = false;
    world.add(curtain, nm, nmLab);

    // ships: arc-length paths from inside the harbour, through the breakwater gap, fanning out
    const ships = Array.from({ length: NSHIP }, (_, k) => {
      const head = (R() - 0.5) * 1.6, sx = (R() - 0.5) * 180, mx = (R() - 0.5) * 30;
      const curve = new THREE.CatmullRomCurve3([[sx, -400], [mx * 0.6, -260], [mx, -170], [mx + Math.sin(head) * 150, -170 + Math.cos(head) * 150],
        [mx + Math.sin(head) * 1300, -170 + Math.cos(head) * 1300]].map(([x, y]) => new THREE.Vector3(x, y, 0)));
      const len = curve.getLength(), big = k % 6 === 1;
      const s = { curve, len, t0: -1.7 + k * 0.215 + (R() - 0.5) * 0.1, v: (k === 0 ? 150 : 100 + R() * 45) * (big ? 0.8 : 1), size: big ? 1.6 : 0.85 + R() * 0.35, gun: GUNS[k % GUNS.length] };
      s.distAtY = (y) => { for (let d = 0; d < len; d += 2) if (curve.getPointAt(d / len).y >= y) return d; return len; };
      s.cross = s.t0 + s.distAtY(NM) / s.v;
      s.crossX = curve.getPointAt(Math.min(1, s.distAtY(NM) / len)).x;
      if (HITS[k] !== undefined) s.hit = s.t0 + s.distAtY(HITS[k]) / s.v;
      return s;
    });
    const makers = ships.filter(s => !(s.hit < s.cross) && s.cross < 6.0).sort((a, b) => a.cross - b.cross);
    makers.forEach((s, k) => S.beat(`cross-${String(k + 1).padStart(2, '0')}`, s.cross));
    ships.filter(s => s.hit !== undefined).sort((a, b) => a.hit - b.hit).forEach((s, k) => S.beat(`hit-${k + 1}`, s.hit));
    const TX = makers[0].cross;

    const chevron = new THREE.Shape([[0, 11], [7, -8], [0, -3.5], [-7, -8]].map(([x, y]) => new THREE.Vector2(x, y)));
    const hulls = new THREE.InstancedMesh(new THREE.ShapeGeometry(chevron), new THREE.MeshBasicMaterial({ color: 0xffffff, depthTest: false, depthWrite: false }), NSHIP);
    hulls.frustumCulled = false;
    const wakes = segments(NSHIP * 21, { additive: true });
    const tracers = segments(NSHIP * 4, { additive: true });
    const flashes = dots(NSHIP, { soft: 1, additive: true }), embers = dots(NSHIP * 3, { soft: 1, additive: true });
    const rings = ships.map(() => { const l = line(circlePts(1, 48), { opacity: 0 }); world.add(l); return l; });
    world.add(wakes, tracers, hulls, embers, flashes);
    [wakes, tracers, hulls, embers, flashes].forEach((o, k) => { o.renderOrder = 10 + k; });
    const dust = motes(40, { seed: 71, alpha: 0.05, z: [-300, 300] });
    S.scene.add(dust);

    // type: fleet status top left, CROSSED (hero) top right with LOST beside it
    const navy = text('REPUBLIC NAVY  /  SORTIE FROM AGRAPOL', { size: 13, font: FONT.mono, weight: 700, tracking: 4, opacity: 0.8 });
    navy.position.set(-560, 258, 0);
    const rows = ['UNDER WAY', 'CREWED', 'TIME'].map((s, k) => {
      const l = text(s, { size: 11, font: FONT.mono, weight: 400, tracking: 3, opacity: 0.5 }); l.position.set(-560, 232 - k * 20, 0);
      const v = text('', { size: 13, font: FONT.mono, weight: 700, tracking: 2, opacity: 0.85, fit: '00:00 Z' }); v.position.set(-440, 232 - k * 20, 0);
      S.hud.add(l, v); return v;
    });
    const crossLab = text('CROSSED', { size: 12, font: FONT.mono, weight: 400, tracking: 4, opacity: 0.6, align: 'right' });
    crossLab.position.set(560, 258, 0);
    const count = text('00', { size: 84, font: FONT.cond, weight: 700, color: C.blue, align: 'right', fit: '00', tracking: -1 });
    count.position.set(564, 210, 0);
    const lostLab = text('LOST', { size: 12, font: FONT.mono, weight: 400, tracking: 4, opacity: 0.6, align: 'right' });
    lostLab.position.set(440, 258, 0);
    const lost = text('00', { size: 84, font: FONT.cond, weight: 700, align: 'right', fit: '00', tracking: -1 });
    lost.position.set(444, 210, 0);
    S.hud.add(scrim(110, 0.8), navy, crossLab, count, lostLab, lost);

    const m4 = new THREE.Matrix4(), q = new THREE.Quaternion(), z = new THREE.Vector3(0, 0, 1), v3 = new THREE.Vector3(), sc = new THREE.Vector3(), c3 = new THREE.Color();
    const distOf = (sh, s) => { // distance travelled; a hit ship coasts to a stop
      const d = sh.v * (s - sh.t0);
      if (sh.hit === undefined || s < sh.hit) return d;
      return sh.v * (sh.hit - sh.t0) + sh.v * 0.35 * (1 - Math.exp(-(s - sh.hit) * 2.8));
    };
    const far = (y) => 1 - seg(y, 420, 760);

    return (s, i, t) => {
      dust.update(t);
      lights.set(0, -24, -146, 0, ink, 16, 0.5 + 0.5 * (Math.floor(t * 1.5) % 2));
      lights.set(1, 22, -144, 0, ink, 16, 0.5 + 0.5 * (Math.floor(t * 1.5 + 0.5) % 2));
      lights.commit();

      wakes.begin(); tracers.begin();
      let crossed = 0, down = 0, gunFlash = GUNS.map(() => 0);
      ships.forEach((sh, k) => {
        const d = distOf(sh, s), on = clamp(d / 40);
        const hitK = sh.hit === undefined ? -1 : S.since(sh.hit), isHit = hitK >= 0;
        const life = isHit ? lerp(0.12, 1, Math.exp(-hitK * 2.2)) : 1;
        if (isHit) down++;
        // tracers from the shore battery in the half second before the hit
        if (sh.hit !== undefined && hitK > -0.5 && hitK < 0.05) {
          const p = sh.curve.getPointAt(Math.min(1, Math.max(0, distOf(sh, s)) / sh.len)), [gx, gy] = sh.gun;
          for (let j = 0; j < 3; j++) {
            const u = clamp((hitK + 0.5) * 2.6 - j * 0.22), u2 = clamp(u + 0.12);
            if (u <= 0 || u >= 1) continue;
            tracers.push(lerp(gx, p.x, u), lerp(gy, p.y, u), lerp(gx, p.x, u2), lerp(gy, p.y, u2), ink, 1.4, ink, 0.2);
          }
          gunFlash[GUNS.indexOf(sh.gun)] = Math.max(gunFlash[GUNS.indexOf(sh.gun)], 0.6 + 0.4 * hash(i, k));
        }
        if (d <= 0) { hulls.setMatrixAt(k, m4.makeScale(0, 0, 0)); flashes.set(k, 0, 0, 0, ink, 0, 0); for (let e = 0; e < 3; e++) embers.set(k * 3 + e, 0, 0, 0, ink, 0, 0); return; }
        const u = Math.min(1, d / sh.len), p = sh.curve.getPointAt(u), tg = sh.curve.getTangentAt(u);
        if (p.y > NM && !(sh.hit < sh.cross)) crossed++;
        const vis = on * far(p.y);
        q.setFromAxisAngle(z, -Math.atan2(tg.x, tg.y));
        hulls.setMatrixAt(k, m4.compose(v3.set(p.x, p.y, 0), q, sc.setScalar(sh.size)));
        c3.copy(isHit && hitK < 0.12 ? flashCol : blue).lerp(ink, isHit ? 0.6 : 0).multiplyScalar(vis * life);
        hulls.setColorAt(k, c3);

        // wake: fading trail plus a thin Kelvin V off the stern
        const wk = vis * (isHit ? Math.exp(-hitK * 1.5) : 1);
        for (let j = 0; j < 16; j++) {
          if (d - (j + 1) * 13 < 0) break;
          const a = sh.curve.getPointAt(clamp((d - j * 13) / sh.len)), b = sh.curve.getPointAt(clamp((d - (j + 1) * 13) / sh.len));
          wakes.push(a.x, a.y, b.x, b.y, blue, 0.55 * wk * Math.pow(1 - j / 16, 1.6), blue, 0.55 * wk * Math.pow(1 - (j + 1) / 16, 1.6));
        }
        const st = [p.x - tg.x * 8 * sh.size, p.y - tg.y * 8 * sh.size], L = 44 * sh.size;
        for (const sd of [-1, 1]) wakes.push(st[0], st[1], st[0] - tg.x * L - tg.y * L * 0.36 * sd, st[1] - tg.y * L + tg.x * L * 0.36 * sd, blue, 0.35 * wk, blue, 0);

        // crossing tick on the 12 NM line
        const ck = S.since(sh.cross);
        if (ck >= 0 && !(sh.hit < sh.cross)) wakes.push(sh.crossX - 30, NM, sh.crossX + 30, NM, blue, 1.8 * Math.exp(-ck * 3.5));

        // hit: flash, ring, then the wreck burns where it stopped
        flashes.set(k, p.x, p.y, 0, flashCol, 70 * Math.exp(-Math.max(0, hitK) * 4) + 10, isHit ? Math.exp(-hitK * 3) : 0);
        for (let e = 0; e < 3; e++) {
          const fl = isHit ? (0.25 + 0.35 * hash(Math.floor(t * 14), k * 3 + e)) * seg(hitK, 0.15, 0.5) : 0;
          embers.set(k * 3 + e, p.x + (e - 1) * 4, p.y + (e % 2) * 3, 0, fire, 10 + 8 * hash(i, e + k), fl);
        }
        const rk = seg(hitK, 0, 0.9);
        rings[k].position.set(p.x, p.y, 0);
        rings[k].scale.setScalar(4 + 60 * E.outExpo(rk));
        rings[k].material.opacity = isHit ? 0.8 * (1 - rk) : 0;
      });
      wakes.end(); tracers.end();
      GUNS.forEach(([gx, gy], k) => guns.set(k, gx, gy, 0, ink, 22, 0.15 + gunFlash[k] * 0.7));
      guns.commit();
      hulls.instanceMatrix.needsUpdate = true;
      if (hulls.instanceColor) hulls.instanceColor.needsUpdate = true;
      flashes.commit(); embers.commit();

      // the line answers every crossing
      const cr = makers.reduce((a, sh) => { const k = S.since(sh.cross); return a + (k >= 0 ? Math.exp(-k * 4) : 0); }, 0);
      const beat = Math.min(1.5, cr);
      nm.material.opacity = 0.5 + 0.35 * Math.min(1, beat);
      nm.material.color.copy(blue).multiplyScalar(1 + 0.9 * beat);
      nmLab.material.color.copy(blue).multiplyScalar(1 + 0.6 * beat);
      curtain.material.uniforms.uGlow.value = 0.16 + 0.28 * beat;
      count.set(String(crossed).padStart(2, '0'));
      count.scale.setScalar(1 + 0.12 * Math.min(1, beat));
      count.material.color.copy(blue).multiplyScalar(1 + 0.5 * Math.min(1, beat));
      lost.set(String(down).padStart(2, '0'));
      const lostK = ships.reduce((a, sh) => { const k = sh.hit === undefined ? -1 : S.since(sh.hit); return a + (k >= 0 ? Math.exp(-k * 6) : 0); }, 0);
      lost.material.color.copy(ink).multiplyScalar(1 + 0.8 * lostK);
      const intro = E.out(seg(s, 0.3, 0.9));
      for (const o of [count, crossLab, navy, lost, lostLab]) o.material.opacity = intro * (o === crossLab || o === lostLab ? 0.6 : o === navy ? 0.8 : 1);
      rows[0].set(String(NSHIP - down).padStart(2, '0')); rows[1].set('51 %'); rows[2].set(`05:${String(41 + Math.floor(s * 2.4)).padStart(2, '0')} Z`);
      rows.forEach((v, k) => { v.material.opacity = 0.85 * E.out(seg(s, 0.5 + k * 0.15, 0.9 + k * 0.15)); });

      const glitch = S.at(TX) ? 0.6 : ships.some(sh => sh.hit !== undefined && S.at(sh.hit)) ? 0.35 : 0;
      return { glitch, shake: 4 * lostK, camera: { target: [0, lerp(-56, -8, E.sine(seg(s, 0, 6.5)))] } };
    };
  },
};
