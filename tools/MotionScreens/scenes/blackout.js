// 03 BLACKOUT: archipelago grid from above. A brownout: the lights dip and come back. Then a plant trips and the failure cascades
// node to node; the last survivors flicker, try to come back and go out. GRID LOAD 112 % becomes NO SUPPLY.
import { THREE, C, FONT, E, NNBSP, seg, lerp, rng, hash, col, line, segments, dots, text, circlePts, motes } from '../look.js';

const ISLANDS = [ // cx, cy, radius, node count
  [-360, -100, 118, 6], [-110, 40, 150, 8], [170, 140, 92, 5], [330, -50, 118, 6], [60, -185, 68, 3], [-470, 95, 58, 3], [470, 105, 46, 2],
];
const CABLES = [[0, 1], [1, 2], [1, 4], [2, 3], [3, 4], [0, 5], [2, 6], [3, 6], [0, 4]];
const TDIP = 1.0, TBACK = 1.45, T0 = 2.4, TEND = 4.55, SURV = [4.85, 5.15, 5.5]; // story seconds
const TLAST = SURV[SURV.length - 1];

export default {
  code: '03', title: 'BLACKOUT', duration: 6.5, bloom: 1.2,
  rigid: [[TDIP - 0.1, TBACK + 0.35], [T0 - 0.15, T0 + 0.7], [TLAST - 0.35, TLAST + 0.6]],
  camera: { push: 0.1, tilt: 0.46, drift: [12, 8] },
  build(S) {
    S.beat('dip', TDIP);
    S.beat('restore', TBACK);
    S.beat('trip', T0);
    S.beat('last-out', TLAST);
    const R = rng(303), ink = col(C.ink), red = col(C.red), warm = col('#f0e2c0');
    const map = new THREE.Group();
    map.scale.setScalar(0.9);
    S.scene.add(map);

    // sea floor: a dot grid a little below the coast (parallax against the land), and a coarse depth grid further down
    const sea = dots(51 * 31);
    let si = 0;
    for (let gx = -25; gx <= 25; gx++) for (let gy = -15; gy <= 15; gy++) sea.set(si++, gx * 40, gy * 40, -50, ink, 1.6, 0.11);
    sea.commit();
    const deep = segments(80, { additive: false });
    deep.begin();
    for (let x = -1200; x <= 1200; x += 160) deep.push(x, -800, x, 800, ink, 1, ink, 1, -160);
    for (let y = -800; y <= 800; y += 160) deep.push(-1200, y, 1200, y, ink, 1, ink, 1, -160);
    deep.end(); deep.material.opacity = 0.035;
    map.add(deep, sea);

    // islands: coast + bathymetry offsets (stepping down) + a topo line
    const shapes = ISLANDS.map(([cx, cy, rad], k) => {
      const ph = [R() * 6, R() * 6, R() * 6, R() * 6];
      const r = (a) => rad * (1 + 0.17 * Math.sin(2 * a + ph[0]) + 0.1 * Math.sin(3 * a + ph[1]) + 0.05 * Math.sin(5 * a + ph[2]) + 0.025 * Math.sin(9 * a + ph[3]));
      const ring = (off, scale = 1, z = 0) => Array.from({ length: 181 }, (_, i) => { const a = i / 180 * Math.PI * 2; return [cx + Math.cos(a) * (r(a) * scale + off), cy + Math.sin(a) * (r(a) * scale + off), z]; });
      map.add(line(ring(0), { opacity: 0.55 }), line(ring(11, 1, -12), { opacity: 0.17 }), line(ring(26, 1, -28), { opacity: 0.08 }), line(ring(44, 1, -46), { opacity: 0.04 }), line(ring(0, 0.6), { opacity: 0.08 }));
      return { cx, cy, r, k };
    });

    // nodes inside each island
    const nodes = [];
    for (const [k, [cx, cy, , n]] of ISLANDS.entries()) {
      let tries = 0;
      for (let made = 0; made < n && tries < 500; tries++) {
        const a = R() * Math.PI * 2, d = Math.sqrt(R()) * 0.72 * shapes[k].r(a);
        const x = cx + Math.cos(a) * d, y = cy + Math.sin(a) * d;
        if (nodes.some(o => Math.hypot(o.x - x, o.y - y) < 36)) continue;
        nodes.push({ x, y, isl: k, plant: made === 0, edges: [], h: made === 0 ? 64 : 22 + R() * 22 });
        made++;
      }
    }
    const edges = [];
    const link = (a, b, cable) => {
      if (edges.some(e => (e.a === a && e.b === b) || (e.a === b && e.b === a))) return;
      const e = { a, b, cable, d: cable ? 0.28 + 0.2 * R() : 0.1 + 0.22 * R(), sp: 0.5 + R() * 0.7, off: R() };
      edges.push(e); nodes[a].edges.push(e); nodes[b].edges.push(e);
    };
    nodes.forEach((o, a) => {
      nodes.map((p, b) => ({ b, d: Math.hypot(p.x - o.x, p.y - o.y) })).filter(q => q.b !== a && nodes[q.b].isl === o.isl)
        .sort((u, v) => u.d - v.d).slice(0, 2).forEach(q => link(a, q.b, false));
    });
    for (const [i1, i2] of CABLES) {
      let best = null;
      nodes.forEach((o, a) => nodes.forEach((p, b) => {
        if (o.isl !== i1 || p.isl !== i2) return;
        const d = Math.hypot(p.x - o.x, p.y - o.y);
        if (!best || d < best.d) best = { a, b, d };
      }));
      link(best.a, best.b, true);
    }

    // cascade: Dijkstra from the capital's plant; the three farthest (one per island) survive it and go out last
    const origin = nodes.findIndex(o => o.isl === 1 && o.plant);
    const dist = nodes.map(() => Infinity);
    dist[origin] = 0;
    const open = new Set(nodes.keys());
    while (open.size) {
      let u = -1;
      for (const v of open) if (u < 0 || dist[v] < dist[u]) u = v;
      open.delete(u);
      for (const e of nodes[u].edges) { const v = e.a === u ? e.b : e.a; dist[v] = Math.min(dist[v], dist[u] + e.d); }
    }
    const order = [...nodes.keys()].sort((a, b) => dist[b] - dist[a]);
    const survivors = [];
    for (const v of order) if (survivors.length < SURV.length && !survivors.some(s => nodes[s].isl === nodes[v].isl)) survivors.push(v);
    const dmax = Math.max(...nodes.map((_, v) => survivors.includes(v) ? 0 : dist[v]));
    nodes.forEach((o, v) => {
      const si = survivors.indexOf(v);
      o.survivor = si >= 0;
      o.fail = o.survivor ? SURV[si] : T0 + Math.pow(dist[v] / dmax, 0.8) * (TEND - T0);
    });

    // city lights: a cluster of tiny warm dots round every node that go out raggedly with it
    const LIGHTS = 26, city = dots(nodes.length * LIGHTS, { additive: true });
    const lights = nodes.flatMap((o, n) => Array.from({ length: LIGHTS }, () => {
      const a = R() * Math.PI * 2, d = 4 + Math.pow(R(), 0.7) * (o.plant ? 26 : 19);
      return { n, x: o.x + Math.cos(a) * d, y: o.y + Math.sin(a) * d, th: R() * 0.9, s: 1.8 + R() * 2.0, a: 0.35 + 0.5 * R() };
    }));

    const land = segments(edges.length, { additive: true });
    const cables = edges.filter(e => e.cable).map(e => {
      const l = line([[nodes[e.a].x, nodes[e.a].y], [nodes[e.b].x, nodes[e.b].y]], { dashed: true, dash: 4, gap: 5 });
      map.add(l); return { e, l };
    });
    const pillars = segments(nodes.length * 2, { additive: true });
    const pulses = dots(edges.length * 2, { soft: 1, additive: true });
    const halos = dots(nodes.length, { soft: 1, additive: true }), cores = dots(nodes.length), tops = dots(nodes.length, { soft: 1, additive: true });
    const rings = nodes.map(o => { const l = line(circlePts(1, 48), { color: C.red }); l.position.set(o.x, o.y, 0); map.add(l); return l; });
    const plantRings = nodes.filter(o => o.plant).map(o => { const l = line(circlePts(10, 32), { opacity: 0.7 }); l.position.set(o.x, o.y, 0); map.add(l); return { o, l }; });
    const sweep = line([[0, -700], [0, 700]], { opacity: 0.08 });
    map.add(land, city, pillars, pulses, halos, cores, tops, sweep);
    const dust = motes(50, { seed: 31, alpha: 0.06 });
    S.scene.add(dust);

    // plant tags with live output
    const tags = [['AGRAPOL', nodes.findIndex(o => o.isl === 0 && o.plant), 380], ['SS-12', nodes.findIndex(o => o.isl === 3 && o.plant), 240],
      ['N-07', survivors[0], 60], ['PLANT 1', origin, 520], ['FELDSPAR', nodes.findIndex(o => o.isl === 2 && o.plant), 170]].map(([s, v, mw]) => {
      const m = text(s, { size: 11, font: FONT.mono, weight: 700, tracking: 2, opacity: 0.6 });
      const w = text(`${mw} MW`, { size: 10, font: FONT.mono, weight: 400, tracking: 1, opacity: 0.5, fit: '000 MW' });
      m.position.set(nodes[v].x + 14, nodes[v].y - 10, 0); w.position.set(nodes[v].x + 14, nodes[v].y - 24, 0);
      map.add(m, w); return { m, w, o: nodes[v], n: v, mw };
    });

    // readouts (screen-locked): load (hero) + frequency top left, nodes online top right
    const loadLab = text('GRID LOAD  /  REPUBLIC NATIONAL GRID', { size: 12, font: FONT.mono, weight: 400, tracking: 3, opacity: 0.6 });
    loadLab.position.set(-560, 258, 0);
    const load = text('NO SUPPLY', { size: 84, font: FONT.cond, weight: 700, tracking: -0.5, fit: 'NO SUPPLY' });
    load.position.set(-564, 206, 0);
    const freq = text('FREQ  50.00 Hz', { size: 13, font: FONT.mono, weight: 700, tracking: 2, opacity: 0.75, fit: 'FREQ  00.00 Hz' });
    freq.position.set(-560, 156, 0);
    const fspark = segments(90, { additive: false });
    const onLab = text('NODES ONLINE', { size: 12, font: FONT.mono, weight: 400, tracking: 4, opacity: 0.6, align: 'right' });
    onLab.position.set(560, 258, 0);
    const online = text('00 / 00', { size: 40, font: FONT.mono, weight: 700, align: 'right' });
    online.position.set(560, 224, 0);
    const nbar = segments(nodes.length, { additive: false });
    S.hud.add(loadLab, load, freq, fspark, onLab, online, nbar);

    const dipK = (s) => seg(s, TDIP, TDIP + 0.06) * (1 - seg(s, TBACK, TBACK + 0.1)); // 1 while the brownout holds
    const level = (o, s, i, n) => { // 0..1 power at node
      let L = 1 - 0.25 * hash(i, n) * seg(s, 1.9, T0);
      const dk = dipK(s);
      if (dk > 0) L = lerp(L, hash(Math.floor(s * 20), n + 5) < 0.15 ? 0.6 : 0.06, dk);
      const back = S.since(TBACK);
      if (back >= 0 && back < 0.5) L *= 1 + 0.35 * Math.exp(-back * 8);
      if (o.survivor) {
        if (s >= o.fail) return lerp(0.03, 1, Math.exp(-S.since(o.fail) * 6));
        if (s > 4.2) return hash(Math.floor(s * 11), n) > 0.42 ? L : 0.1;
        return L;
      }
      if (s < o.fail) return L;
      return lerp(0.03, 1, Math.exp(-S.since(o.fail) * 5));
    };
    const tmp = new THREE.Color(), tmp2 = new THREE.Color();
    const fq = (s) => { // grid frequency, Hz
      let f = 50 + 0.012 * (hash(Math.floor(s * 7), 2) - 0.5) - 0.25 * seg(s, 1.7, T0);
      f -= 0.85 * dipK(s);
      if (s > T0) f -= 2.4 * E.out(seg(s, T0, TEND));
      return f;
    };

    return (s, i, t) => {
      map.rotation.z = lerp(-0.07, 0.05, E.sine(seg(s, 0, 6.5)));
      dust.update(t);
      const lv = nodes.map((o, n) => level(o, s, i, n));
      const burn = nodes.map(o => { const k = S.since(o.fail); return k >= 0 ? Math.exp(-k * 6) : 0; });

      land.begin();
      let pi = 0;
      for (const e of edges) {
        const a = nodes[e.a], b = nodes[e.b], live = Math.min(lv[e.a], lv[e.b]), hot = Math.max(burn[e.a], burn[e.b]);
        tmp.copy(ink).multiplyScalar(0.06 + 0.34 * live).add(tmp2.copy(red).multiplyScalar(0.9 * hot));
        if (!e.cable) land.push(a.x, a.y, b.x, b.y, tmp, 1);
        for (let q = 0; q < 2; q++) {
          const ph = (t * e.sp + e.off + q * 0.5) % 1;
          pulses.set(pi++, lerp(a.x, b.x, ph), lerp(a.y, b.y, ph), 0, ink, 7, 0.75 * Math.pow(live, 3));
        }
      }
      land.end();
      pulses.commit();
      for (const { e, l } of cables) {
        const live = Math.min(lv[e.a], lv[e.b]), hot = Math.max(burn[e.a], burn[e.b]);
        l.material.color.copy(ink).lerp(red, hot); l.material.opacity = 0.08 + 0.4 * live + 0.6 * hot;
      }

      lights.forEach((p, k) => {
        const L = lv[p.n], on = L > p.th + 0.08 * hash(i, k) ? 1 : 0.04 * L;
        city.set(k, p.x, p.y, 0, burn[p.n] > 0.3 ? red : warm, p.s, p.a * on * Math.min(1, L * 1.2));
      });
      city.commit();

      // a telemetry sweep crosses the map; nodes it passes tick brighter
      const sx = -760 + ((t * 420) % 1700);
      sweep.position.x = sx;
      let up = 0;
      pillars.begin();
      nodes.forEach((o, n) => {
        const L = lv[n], hot = burn[n], ping = Math.exp(-Math.abs(o.x - sx) / 14);
        if (L > 0.5) up++;
        tmp.copy(ink).lerp(red, Math.min(1, hot * 1.5 + (s >= o.fail ? 0.5 : 0)));
        cores.set(n, o.x, o.y, 0, tmp, (o.plant ? 8 : 5.5) + 6 * hot, 0.25 + 0.75 * Math.max(L, hot) + 0.3 * ping * L);
        halos.set(n, o.x, o.y, 0, hot > 0.02 ? red : ink, 34 + 50 * hot, 0.26 * L + 0.9 * hot);
        const h = o.h * Math.min(1.2, L) + 30 * hot, pc = hot > 0.02 ? red : ink;
        pillars.push(o.x, o.y, o.x, o.y, pc, 0.95 * Math.max(L, hot), pc, 0.08, 0);
        tops.set(n, o.x, o.y, h, pc, 6, 0.5 * Math.max(L, hot) * (L > 0.2 || hot > 0.05 ? 1 : 0));
        const rk = seg(S.since(o.fail), 0, 0.75);
        rings[n].scale.setScalar(5 + 48 * E.outExpo(rk));
        rings[n].material.opacity = s < o.fail ? 0 : 0.85 * (1 - rk);
        o._h = h;
      });
      pillars.end();
      { // lift the top vertex of every pillar to its height
        const p = pillars.geometry.attributes.position.array;
        nodes.forEach((o, n) => { p[n * 6 + 5] = o._h; });
        pillars.geometry.attributes.position.needsUpdate = true;
      }
      cores.commit(); halos.commit(); tops.commit();
      for (const { o, l } of plantRings) l.material.opacity = 0.15 + 0.55 * level(o, s, i, nodes.indexOf(o));
      for (const tg of tags) {
        const L = lv[tg.n];
        tg.m.material.opacity = 0.2 + 0.5 * Math.min(1, L);
        const mw = s >= tg.o.fail ? 0 : Math.round(tg.mw * Math.min(1, L) * (1 + 0.02 * (hash(Math.floor(t * 6), tg.mw) - 0.5)));
        tg.w.set(`${mw} MW`);
        tg.w.material.color.copy(mw === 0 && s > T0 ? red : ink);
        tg.w.material.opacity = 0.35 + 0.4 * (mw === 0 && s > T0 ? 1 : Math.min(1, L));
      }

      // readouts
      const pct = Math.round(lerp(96, 112, E.inOut(seg(s, 0.4, T0))));
      const ns = s >= TLAST, dk = dipK(s);
      load.set(ns ? 'NO SUPPLY' : dk > 0.5 ? 'BROWNOUT' : `${pct}${NNBSP}%`);
      load.material.color.copy(ns || pct >= 104 || dk > 0.5 ? red : ink);
      load.material.opacity = E.out(seg(s, 0.3, 0.8)) * (!ns && s > T0 && Math.floor(t * 4) % 2 ? 0.5 : 1);
      load.scale.setScalar(ns ? lerp(1.2, 1, E.outExpo(seg(S.since(TLAST), 0, 0.35))) : 1);
      load.material.color.multiplyScalar(ns ? 1 + 0.8 * Math.exp(-S.since(TLAST) * 5) : 1);
      loadLab.typeOn(seg(s, 0.15, 0.8));
      const f = fq(s);
      freq.set(ns ? 'FREQ  --.-- Hz' : `FREQ  ${f.toFixed(2)} Hz`);
      freq.material.color.copy(f < 49.5 || ns ? red : ink);
      freq.material.opacity = 0.75 * E.out(seg(s, 0.4, 0.9));
      fspark.begin(); // the last seconds of frequency as a trace under the readout
      for (let k = 0; k < 80; k++) {
        const a = s - (80 - k) * 0.03, b = a + 0.03;
        if (a < 0.4) continue;
        const y = (v) => 140 + Math.max(-22, (v - 50) * 9);
        fspark.push(-560 + k * 2.6, y(fq(a)), -560 + (k + 1) * 2.6, y(fq(b)), fq(b) < 49.5 ? red : ink, ns && b > TLAST ? 0 : 0.7);
      }
      fspark.end();
      loadLab.material.opacity = 0.6 * E.out(seg(s, 0.2, 0.7));
      online.set(`${String(ns ? 0 : up).padStart(2, '0')} / ${nodes.length}`);
      online.material.color.copy(up < nodes.length ? red : ink);
      online.material.opacity = onLab.material.opacity = E.out(seg(s, 0.4, 0.9));
      nbar.begin(); // one tick per node, lit while it is up
      nodes.forEach((o, n) => { const x = 560 - (nodes.length - n) * 6; nbar.push(x, 192, x, 200, lv[n] > 0.5 ? ink : red, lv[n] > 0.5 ? 0.7 : 0.6); });
      nbar.end();

      const glitch = S.at(TLAST, 2) ? 1 : S.at(T0) ? 0.6 : S.at(TDIP) ? 0.4 : S.at(TBACK) ? 0.25 : 0;
      return {
        glitch, bloom: 1.2 + 0.4 * (S.since(TBACK) >= 0 ? Math.exp(-S.since(TBACK) * 6) : 0),
        shake: (S.since(T0) >= 0 ? 9 * Math.exp(-S.since(T0) * 6) : 0) + (S.since(TLAST) >= 0 ? 7 * Math.exp(-S.since(TLAST) * 7) : 0),
        camera: { target: [0, lerp(-5, -22, E.sine(seg(s, 0, 6.5)))] },
      };
    };
  },
};
