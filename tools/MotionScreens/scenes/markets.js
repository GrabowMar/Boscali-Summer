// 01 MARKETS: one week on the Boscali exchange. The BSX line breaks and falls off a cliff, the field follows, the counter
// rolls down and slams on -33.4 % ("lost a third of its value in a week"); trading halts; the money is gone, nobody knows where.
import { THREE, C, FONT, E, MINUS, NNBSP, seg, lerp, rng, hash, noise1, col, mixCol, line, segments, dots, rect, text, canvasPlane, motes } from '../look.js';

const TC = 3.2, TS = 4.6;                  // story seconds: the break, the counter locks
const head = (tau) => -470 + 100 * tau;    // chart x of chart-time tau (one story second = one tau)
const SR = 60, T0 = -7, T1 = 7.1;          // samples per tau, sampled span
const NS = Math.round((T1 - T0) * SR) + 1;
const tauOf = (i) => T0 + i / SR;
const shown = (s) => Math.floor((s - T0) * SR) + 1;
const OPEN = 4212.6, FINAL = -33.4;
const DAYS = ['MON', 'TUE', 'WED', 'THU', 'FRI'], DAY = (T1 - T0) / 5;
const when = (tau) => { // trading day and clock at chart-time tau
  const d = Math.min(4, Math.floor((tau - T0) / DAY)), m = 540 + Math.floor(((tau - T0) / DAY - d) * 480);
  return `${DAYS[d]} ${String(9 + d).padStart(2, '0')} MAR  ${String(Math.floor(m / 60)).padStart(2, '0')}:${String(m % 60).padStart(2, '0')}`;
};

const pre = (tau) => 34 + 13 * tau + 14 * (noise1(tau * 1.7, 3) - 0.5) + 5 * (noise1(tau * 7, 4) - 0.5);
const yc = pre(TC);
function bsx(tau) {
  if (tau <= TC) return pre(tau);
  const k = tau - TC;
  return yc - 185 * E.out(seg(k, 0, 0.42)) - 52 * E.sine(seg(k, 0.42, 3.9)) + 15 * Math.sin(seg(k, 0.9, 1.9) * Math.PI)
    + 12 * (noise1(tau * 9, 5) - 0.5) * seg(k, 0, 0.3);
}
const priceAt = (y) => OPEN * (1 + FINAL / 100) + (y - bsx(T1)) * 5.6;
const fmt = (v) => v.toFixed(2).replace(/\B(?=(\d{3})+\.)/, ' ');
const pctStr = (v) => `${v >= 0 ? '+' : MINUS}${Math.abs(v).toFixed(1)}${NNBSP}%`;

const SECTORS = [['BANKS', 1.9, -51.2], ['SHIPPING', 2.4, -44.8], ['PORTS', 1.2, -39.5], ['ENERGY', 0.8, -27.3], ['TELECOM', 0.4, -18.6]];
const TICK = [['AGB', 41.2], ['BKB', 12.04], ['CVL', 88.5], ['FDS', 7.31], ['HLY', 233.0], ['IGN', 19.9], ['KRS', 5.12], ['MRT', 64.75],
  ['NVP', 140.1], ['OPL', 9.86], ['PRT', 27.4], ['RPB', 3.07], ['SFC', 55.6], ['TLM', 18.25], ['VRG', 102.3], ['ZNC', 11.4]];

export default {
  code: '01', title: 'MARKETS', duration: 7, bloom: 1.1,
  rigid: [[TC - 0.2, TS + 0.5]],
  camera: { push: 0.09, drift: [12, 7], tilt: 0.1 },
  build(S) {
    S.beat('break', TC);
    S.beat('slam', TS);
    const R = rng(11), ink = col(C.ink), red = col(C.red), ac = new THREE.Color();

    // far layer: other markets, already drawn in full, behind and larger (parallax against the BSX layer)
    const far = new THREE.Group();
    far.position.set(0, 10, -520); far.scale.setScalar(1.42);
    S.scene.add(far);
    const farLines = [];
    for (let s = 0; s < 22; s++) {
      const base = R() * 420 - 210, amp = 30 + R() * 50, f = 0.5 + R() * 1.4, seed = 300 + s, drop = 60 + R() * 200, delay = 0.2 + R() * 1.6;
      const pts = Array.from({ length: 300 }, (_, j) => {
        const x = -900 + j * 6, tau = T0 + j * 0.047;
        return [x, base + amp * (noise1(tau * f, seed) - 0.5)];
      });
      const l = line(pts, { opacity: 0.035 + R() * 0.05 });
      far.add(l);
      farLines.push({ l, pts, base, drop, delay });
    }

    // chart grid: price rows edge to edge, day columns (bright) and hour columns (faint), day labels under the volume
    const world = new THREE.Group();
    S.scene.add(world);
    const rows = [];
    for (let y = -220; y <= 140; y += 45) {
      const l = line([[-1400, y], [1400, y]], { opacity: 0.075 });
      const lab = text(fmt(priceAt(y)).replace(/\.\d+$/, ''), { size: 11, font: FONT.mono, weight: 400, opacity: 0.4, align: 'right', fit: '0 000' });
      lab.position.set(570, y + 8, 0);
      world.add(l, lab);
      rows.push({ l, lab, y });
    }
    for (let d = 0; d <= 5; d++) {
      const x = head(T0 + d * DAY);
      world.add(line([[x, -236], [x, 150]], { opacity: d % 5 ? 0.11 : 0.06 }));
      for (let h = 1; h < 8; h++) world.add(line([[x + h * DAY * 100 / 8, -236], [x + h * DAY * 100 / 8, 150]], { opacity: 0.03 }));
      if (d < 5) {
        const lab = text(`${DAYS[d]} ${String(9 + d).padStart(2, '0')}`, { size: 10, font: FONT.mono, weight: 400, tracking: 2, opacity: 0.38 });
        lab.position.set(x + 6, -247, 0);
        world.add(lab);
      }
    }

    // the field: thin price lines that follow the index down with a stagger
    const field = [];
    for (let s = 0; s < 46; s++) {
      const base = R() * 380 - 200, slope = (R() - 0.4) * 9, amp = 18 + R() * 50, f = 0.6 + R() * 1.8, seed = 20 + s;
      const delay = 0.12 + Math.pow(R(), 1.5) * 1.6, dur = 0.5 + R() * 1.4, drop = 60 + R() * 200;
      const pts = [];
      for (let i = 0; i < NS; i++) {
        const tau = tauOf(i);
        pts.push([head(tau), base + slope * tau + amp * (noise1(tau * f, seed) - 0.5) + 6 * (noise1(tau * 8, seed + 99) - 0.5)
          - drop * E.inOut(seg(tau, TC + delay, TC + delay + dur))]);
      }
      const l = line(pts, { opacity: 0.06 + R() * 0.18 });
      world.add(l);
      field.push(l);
    }

    // BSX: thick line, broken at the cliff into two pieces
    const preN = Math.round((TC - T0) * SR) + 1;
    const all = Array.from({ length: NS }, (_, i) => [head(tauOf(i)), bsx(tauOf(i))]);
    const bsxA = line(all.slice(0, preN), { width: 2.6 });
    const bsxB = line(all.slice(preN + 2), { width: 2.6 });
    const glowA = line(all.slice(0, preN), { width: 9, opacity: 0.12 });
    world.add(glowA, bsxA, bsxB);

    const cursor = line([[0, -236], [0, 150]], { opacity: 0.22 });
    const scan = line([[0, -236], [0, 150]], { opacity: 0.1 });
    const when1 = text(when(0), { size: 10, font: FONT.mono, weight: 400, tracking: 1, opacity: 0.55, fit: 'WED 11 MAR  14:20' });
    const vol = segments(220, { additive: true });
    const guide = line([[0, 0], [1, 0]], { dashed: true, dash: 3, gap: 4, opacity: 0.5 });
    const marker = dots(2, { soft: 0 }), halo = dots(1, { soft: 1, additive: true });
    const ring = line(Array.from({ length: 65 }, (_, i) => [Math.cos(i / 64 * Math.PI * 2), Math.sin(i / 64 * Math.PI * 2)]), { color: C.red });
    const ring2 = ring.clone(); ring2.material = ring.material.clone();
    const tag = text('BSX', { size: 22, font: FONT.cond, weight: 700 });
    const tagPx = text('4 212.60', { size: 12, font: FONT.mono, weight: 700, fit: '0 000.00' });
    const axisTag = line([[-30, -8], [30, -8], [36, 0], [30, 8], [-30, 8]], { closed: true, color: C.blue });
    const axisPx = text('4 212', { size: 11, font: FONT.mono, weight: 700, align: 'right', fit: '0 000' });
    world.add(cursor, scan, when1, vol, guide, halo, marker, ring, ring2, tag, tagPx, axisTag, axisPx);
    const dust = motes(70, { seed: 12 });
    S.scene.add(dust);

    // screen-locked type: the hero counter top left, its sub-rows, the halt tag; sectors top right; the tape in the top band
    const label = text('BSX  /  BOSCALI STOCK EXCHANGE  /  ALL-SHARE INDEX', { size: 12, font: FONT.mono, weight: 400, tracking: 3, opacity: 0.6 });
    label.position.set(-560, 258, 0);
    const counter = text(`BSX ${pctStr(FINAL)}`, { size: 124, font: FONT.cond, weight: 700, tracking: -1, fit: `BSX ${pctStr(-88.8)}` });
    counter.position.set(-566, 182, 0);
    const stats = text('', { size: 12, font: FONT.mono, weight: 400, tracking: 2, opacity: 0.6, fit: 'OPEN 4 212.60    LOW 0 000.00    VOL 000.0 M' });
    stats.position.set(-560, 116, 0);
    const outflow = text('CAPITAL OUTFLOW  USD 4.1 BN   /   DESTINATION  UNTRACED', { size: 12, font: FONT.mono, weight: 700, tracking: 2, color: C.red });
    outflow.position.set(-560, 97, 0);
    const halt = new THREE.Group();
    const haltBox = rect(196, 34, C.red, 1), haltLine = line([[-98, -17], [98, -17], [98, 17], [-98, 17]], { closed: true, color: C.red });
    const haltTxt = text('TRADING HALTED', { size: 20, font: FONT.mono, weight: 700, tracking: 3, color: C.bg, align: 'center' });
    haltTxt.position.y = 1;
    halt.add(haltBox, haltLine, haltTxt);
    S.hud.add(label, counter, stats, outflow, halt);

    const secHead = text('SECTORS  /  WEEK', { size: 11, font: FONT.mono, weight: 400, tracking: 3, opacity: 0.55 });
    secHead.position.set(300, 258, 0);
    S.hud.add(secHead);
    const zero = line([[480, 244], [480, 138]], { opacity: 0.25 });
    S.hud.add(zero);
    const secs = SECTORS.map(([name, up, down], k) => {
      const y = 232 - k * 22;
      const n = text(name, { size: 12, font: FONT.mono, weight: 400, tracking: 2, opacity: 0.7 }); n.position.set(300, y, 0);
      const v = text(pctStr(down), { size: 13, font: FONT.mono, weight: 700, align: 'right', fit: pctStr(-88.8) }); v.position.set(566, y, 0);
      const b = rect(1, 6, C.ink); b.geometry.translate(0.5, 0, 0); b.position.set(480, y - 1, 0);
      S.hud.add(n, v, b);
      return { n, v, b, up, down, at: TC + 0.15 + k * 0.16 };
    });

    const TAPE = 1500;
    const tape = canvasPlane(2 * TAPE, 22, (g, w, h, crashed = false) => {
      g.font = `400 12px ${FONT.mono}`; g.textBaseline = 'middle'; g.letterSpacing = '1px';
      for (let rep = 0; rep < 2; rep++) {
        let x = rep * TAPE;
        for (const [k, [sym, p]] of TICK.entries()) {
          const ch = crashed ? -(12 + 40 * hash(k, 3)) : (hash(k, 5) - 0.35) * 3;
          g.fillStyle = 'rgba(230,225,211,0.75)'; g.fillText(sym, x, h / 2); x += 36;
          g.fillStyle = 'rgba(230,225,211,0.5)'; g.fillText((p * (1 + ch / 100)).toFixed(2), x, h / 2); x += 52;
          g.fillStyle = crashed ? C.red : ch < 0 ? 'rgba(230,225,211,0.45)' : 'rgba(111,168,220,0.9)';
          g.fillText(`${ch < 0 ? MINUS : '+'}${Math.abs(ch).toFixed(1)}`, x, h / 2); x += 56;
          if (x > (rep + 1) * TAPE - 140) break;
        }
      }
    });
    tape.position.y = 304;
    const tapeRule = line([[-640, 292], [640, 292]], { opacity: 0.1 });
    S.hud.add(tape, tapeRule);
    let tapeCrashed = false;

    return (s, i, t) => {
      const n = shown(s), hx = head(s), hy = bsx(s), crash = E.inOut(seg(s, TC, TC + 0.5)), sinceC = S.since(TC), sinceS = S.since(TS);
      mixCol(C.blue, C.red, crash, ac);
      dust.update(t);

      for (const l of field) l.reveal(n);
      bsxA.reveal(Math.min(n, preN)); glowA.reveal(Math.min(n, preN));
      bsxB.reveal(n - preN - 2);
      bsxA.material.color.copy(ac); bsxB.material.color.copy(ac); glowA.material.color.copy(ac);
      glowA.material.opacity = 0.12 + 0.25 * (sinceC >= 0 ? Math.exp(-sinceC * 3) : 0);
      for (const fl of farLines) { // the far markets drop a beat later, together
        const k = E.inOut(seg(s, TC + fl.delay, TC + fl.delay + 1.2));
        fl.l.position.y = -fl.drop * k;
        fl.l.material.color.copy(ink).lerp(red, 0.5 * k);
      }

      cursor.position.x = hx;
      scan.position.x = head(T0) + ((t * 260) % 1500);
      when1.set(when(s)); when1.position.set(hx + 6, -224, 0);

      vol.begin();
      for (let tau = T0; tau <= s; tau += 0.1) {
        const post = tau > TC, h = post ? 18 + 46 * Math.pow(hash(tau * 10, 7), 2) * Math.exp(-(tau - TC) * 0.5) : 3 + 14 * Math.pow(hash(tau * 10, 7), 3);
        vol.push(head(tau), -236, head(tau), -236 + h, post ? red : ink, post ? 0.7 : 0.28);
      }
      vol.end();

      // grid + numbers flicker after the break
      const shaky = seg(s, TC, TC + 0.1) * (1 - 0.6 * seg(s, TC + 2.5, 6));
      for (const [k, r] of rows.entries()) {
        const blink = hash(i, k * 3 + 1) < 0.18 * shaky ? 0.15 : 1;
        r.l.material.opacity = 0.075 * blink * (1 + shaky * hash(i, k));
        r.lab.material.opacity = 0.4 * (hash(i, k + 50) < 0.3 * shaky ? 0.1 : 1);
        r.lab.material.color.copy(r.y > hy && s > TC ? red : ink);
      }

      // the counter: small blue before; at the break it jumps to full size and rolls with flickering digits, then locks on -33.4
      const roll = E.out(seg(s, TC, TS));
      const pct = s < TC ? 2.1 * E.sine(seg(s, 0, TC)) + 0.25 * (hash(Math.floor(t * 6), 1) - 0.5)
        : s < TS ? lerp(2.1, FINAL, roll) + (hash(i, 9) - 0.5) * 3.2 * (1 - roll) : FINAL;
      const price = OPEN * (1 + pct / 100) + (s < TS ? 0 : (hy - bsx(TS)) * 5.6); // after the lock the tag follows the line
      counter.set(`BSX ${pctStr(pct)}`);
      counter.scale.setScalar(s < TC ? 0.42 : sinceS >= 0 ? lerp(1.16, 1, E.outExpo(seg(sinceS, 0, 0.35))) : lerp(1.1, 1, E.outExpo(seg(sinceC, 0, 0.3))));
      counter.position.y = s < TC ? 214 : 182;
      counter.material.color.copy(ac).multiplyScalar(sinceS >= 0 ? 1 + 0.9 * Math.exp(-sinceS * 6) : 1);
      counter.material.opacity = E.out(seg(s, 0.4, 1.0));
      label.typeOn(seg(s, 0.15, 0.9)); label.material.opacity = 0.6;
      const low = Math.min(OPEN, price);
      stats.set(`OPEN ${fmt(OPEN)}    LOW ${fmt(low)}    VOL ${(180 + 40 * s + (s > TC ? 260 * E.out(seg(s, TC, 6)) : 0)).toFixed(1)} M`);
      stats.material.opacity = 0.6 * E.out(seg(s, 0.6, 1.2)) * (s > TC && hash(i, 31) < 0.12 ? 0.3 : 1);
      stats.position.y = s < TC ? 150 : 116;
      outflow.typeOn(seg(sinceS, 0.55, 1.5)); outflow.material.opacity = sinceS > 0.55 ? 0.9 : 0;

      // TRADING HALTED stamps in to the right of the locked counter
      const hk = seg(sinceS, 0, 0.18);
      halt.visible = sinceS >= 0;
      halt.position.set(-566 + counter.textWidth * counter.scale.x + 120, 190, 0);
      halt.scale.setScalar(lerp(1.5, 1, E.outExpo(hk)));
      const settle = seg(sinceS, 0.15, 0.45); // stamped in filled, then it settles to an outline with red type
      haltBox.material.opacity = E.out(hk) * (1 - settle);
      haltTxt.material.color.copy(col(C.bg)).lerp(red, settle);
      haltTxt.material.opacity = E.out(hk) * (settle < 1 || Math.floor(t * 2.5) % 2 ? 1 : 0.6);
      haltLine.material.opacity = E.out(hk);

      // sectors: small gains in ink, then one after another they fall into red bars
      for (const sc of secs) {
        const k = E.outExpo(seg(s, sc.at, sc.at + 0.6)), v = lerp(sc.up, sc.down, k) + (s < sc.at ? 0.3 * (hash(Math.floor(t * 5), sc.up * 10) - 0.5) : 0);
        sc.v.set(pctStr(v));
        sc.v.material.color.copy(v < 0 ? red : ink);
        sc.b.scale.x = Math.max(0.5, Math.abs(v) * 1.55);
        sc.b.position.x = v < 0 ? 480 - Math.abs(v) * 1.55 : 480;
        sc.b.material.color.copy(v < 0 ? red : col(C.blue));
        for (const o of [sc.n, sc.v, sc.b]) o.material.opacity = E.out(seg(s, 0.7, 1.3)) * (o === sc.n ? 0.7 : 1);
      }
      secHead.material.opacity = zero.material.opacity = 0.5 * E.out(seg(s, 0.6, 1.2));

      if (s >= TC && !tapeCrashed) { tape.redraw(true); tapeCrashed = true; }
      tape.position.x = -640 - ((t * 70) % TAPE) + TAPE;
      tape.material.opacity = tapeRule.material.opacity = E.out(seg(s, 0.2, 0.8));
      tapeRule.material.opacity *= 0.1;

      // head marker, price tag, dashed guide to the axis and the axis price flag
      marker.set(0, hx, hy, 0, ac, 7, 1); marker.set(1, hx, hy, 0, col(C.bg), 3, 1); marker.commit();
      halo.set(0, hx, hy, 0, ac, 46 + 30 * crash, 0.35 + 0.3 * crash); halo.commit();
      tag.position.set(hx + 14, hy + 20, 0); tag.material.color.copy(ac);
      tagPx.set(fmt(price)); tagPx.position.set(hx + 14, hy + 3, 0); tagPx.material.color.copy(ac);
      const gp = guide.geometry.attributes.position;
      gp.setXYZ(0, hx + 86, hy, 0); gp.setXYZ(1, 498, hy, 0); gp.needsUpdate = true; guide.computeLineDistances();
      guide.material.color.copy(ac);
      axisTag.position.set(536, hy, 0); axisTag.material.color.copy(ac); axisPx.material.color.copy(ac);
      axisPx.set(fmt(price).replace(/\.\d+$/, '')); axisPx.position.set(565, hy + 0.5, 0);

      for (const [k, rg] of [ring, ring2].entries()) {
        const rk = seg(sinceC - k * 0.12, 0, 0.9);
        rg.position.set(head(TC), yc, 0);
        rg.scale.setScalar(6 + (90 + 60 * k) * E.outExpo(rk));
        rg.material.opacity = sinceC - k * 0.12 < 0 ? 0 : (0.9 - 0.4 * k) * (1 - rk);
      }

      const glitch = S.at(TC, 2) ? 1 : S.at(TS, 1) ? 0.5 : S.at(1.9) ? 0.35 : S.at(5.6) ? 0.4 : 0;
      return {
        glitch,
        shake: (sinceC >= 0 ? 16 * Math.exp(-sinceC * 7) : 0) + (sinceS >= 0 ? 10 * Math.exp(-sinceS * 9) : 0),
        camera: { target: [lerp(-70, 90, E.sine(seg(s, 0, 7))), lerp(14, -28, E.inOut(seg(s, TC, TC + 1.8)))] },
      };
    };
  },
};
