// 02 DEBT: the national debt as a clock. Odometer reels spin up over an accelerating dial and a scrolling ledger; the rating slips;
// the DEBT/GDP fill crosses DEFAULT (rating D, red sweep); the 10Y yield ticks to 31.2 %; the reels lock on USD 14 211 000 000.
import { THREE, C, FONT, E, MINUS, NNBSP, seg, lerp, rng, hash, col, line, segments, rect, text, canvasPlane, circlePts, motes } from '../look.js';

const V0 = 10_230_000_000, V1 = 14_211_000_000, TL = 5.0; // reels lock at TL
const value = (s) => lerp(V0, V1, 1 - Math.pow(1 - seg(s, 0.3, TL), 4));
const fill = (s) => 0.93 * E.inOut(seg(s, 0.5, 4.7));
const MARK = 0.78, BX0 = -520, BX1 = 520, BY = 6;
let TD = 0; // story time the fill crosses DEFAULT
for (let s = 0; s < 6; s += 1 / 600) if (fill(s) >= MARK) { TD = s; break; }
const TG = 1.9;  // first downgrade
const TICKS = Array.from({ length: 22 }, (_, k) => ({ s: 0.8 + 3.9 * Math.sqrt(k / 21), v: 8.4 + 22.8 * Math.pow(k / 21, 1.6) }));
const pct = (v, d = 1) => `${v.toFixed(d)}${NNBSP}%`;

export default {
  code: '02', title: 'DEBT', duration: 6, bloom: 1.1,
  rigid: [[TD - 0.2, TD + 0.9], [TL - 0.15, TL + 0.4]],
  camera: { push: 0.1, drift: [12, 6] },
  build(S) {
    S.beat('downgrade', TG);
    S.beat('default', TD);
    S.beat('locked', TL);
    const ink = col(C.ink), red = col(C.red), amber = col(C.amber), ac = new THREE.Color();

    // far: the ledger, scrolling upward behind everything (texture offset, no redraw)
    const LR = rng(202), ITEMS = ['T-BILL 2034-06', 'BOND 2041 4.25 %', 'IMF SBA TRANCHE 3', 'PORT AUTH NOTE', 'GRID FUEL ARREARS', 'ARMY PAYROLL ADV',
      'EUROBOND 2036', 'CENTRAL BANK REPO', 'PENSION FUND IOU', 'NAVY REFIT LOAN', 'T-BILL 2034-09', 'BRIDGE LOAN (SGP)'];
    const ledger = canvasPlane(1900, 1100, (g, w, h) => {
      g.font = `400 13px ${FONT.mono}`; g.textBaseline = 'middle'; g.fillStyle = '#fff';
      for (let c = 0; c < 3; c++) for (let r = 0; r < 52; r++) {
        const x = 40 + c * 630, y = 12 + r * 21.1;
        g.globalAlpha = 0.45 + 0.55 * LR();
        g.fillText(`${String(1000 + Math.floor(LR() * 8999))}  ${ITEMS[Math.floor(LR() * ITEMS.length)].padEnd(18)}`, x, y);
        g.textAlign = 'right'; g.fillText(`${MINUS}${Math.floor(20 + LR() * 900)} ${String(Math.floor(LR() * 1000)).padStart(3, '0')} 000`, x + 560, y); g.textAlign = 'left';
      }
    });
    const lt = ledger.material.map;
    lt.wrapS = lt.wrapT = THREE.RepeatWrapping;
    ledger.position.set(0, 0, -620); ledger.scale.setScalar(1.48);
    ledger.material.opacity = 0.11;
    S.scene.add(ledger);

    // the clock behind the numbers: dial, ticks, accelerating sweep hand with a fading trail
    const dial = new THREE.Group();
    dial.position.set(0, 70, -200);
    const ticks = segments(250, { additive: false });
    ticks.begin();
    for (let k = 0; k < 240; k++) {
      const a = k / 240 * Math.PI * 2, r0 = k % 20 === 0 ? 300 : k % 4 === 0 ? 318 : 326;
      ticks.push(Math.cos(a) * r0, Math.sin(a) * r0, Math.cos(a) * 336, Math.sin(a) * 336, ink, k % 4 === 0 ? 1 : 0.5);
    }
    ticks.end();
    ticks.material.opacity = 0.16;
    const outer = line(circlePts(352, 200), { opacity: 0.08 }), inner = line(circlePts(258, 200), { opacity: 0.05 }), inner2 = line(circlePts(140, 140), { dashed: true, dash: 3, gap: 6, opacity: 0.08 });
    inner2.computeLineDistances();
    const hand = segments(14, { additive: true });
    const numerals = Array.from({ length: 12 }, (_, k) => {
      const a = Math.PI / 2 - k / 12 * Math.PI * 2, m = text(String(k * 5).padStart(2, '0'), { size: 11, font: FONT.mono, weight: 400, opacity: 0.25, align: 'center' });
      m.position.set(Math.cos(a) * 280, Math.sin(a) * 280, 0); return m;
    });
    dial.add(outer, inner, inner2, ticks, hand, ...numerals);
    S.scene.add(dial);
    const dust = motes(60, { seed: 22, alpha: 0.07 });
    S.scene.add(dust);

    const label = text('REPUBLIC DEBT  /  GROSS', { size: 15, font: FONT.mono, weight: 700, tracking: 9, align: 'center' });
    label.position.set(0, 196, 0);

    // odometer: each reel turns continuously; fast reels smear, slow reels roll, settled reels lock
    const CELL = 62, GAP = 24, LH = 120, GROUPS = [1, 4, 7];
    const odo = canvasPlane(1000, 150, (g, w, h, s = 0, i = 0) => {
      const V = value(s), rate = (value(s + 0.002) - V) / 0.002;
      const digitsW = 11 * CELL + 3 * GAP, x0 = (w - digitsW - 110) / 2 + 110, cy = h / 2 + 4;
      g.fillStyle = '#fff';
      g.globalAlpha = 0.6; g.font = `700 46px ${FONT.cond}`; g.textAlign = 'right'; g.textBaseline = 'middle';
      g.fillText('USD', x0 - 26, cy + 14);
      g.font = `700 108px ${FONT.mono}`; g.textAlign = 'center';
      for (let j = 0; j < 11; j++) {
        const p = 10 - j, x = x0 + j * CELL + GROUPS.filter(q => j > q).length * GAP + CELL / 2;
        const r = (V / 10 ** p) % 10, speed = rate / 10 ** p;
        g.save();
        g.beginPath(); g.rect(x - CELL / 2, cy - LH / 2, CELL, LH); g.clip();
        if (speed > 6) { // smeared: one flickering digit with vertical motion blur
          const d = String(Math.floor(hash(i, j * 13) * 10));
          for (let k = -3; k <= 3; k++) { g.globalAlpha = 0.13; g.fillText(d, x, cy + k * 9 + 6); }
        } else { // mechanical click: hold, then a quick roll to the next digit
          const d0 = Math.floor(r), f = speed < 0.05 ? 0 : E.inOut(seg(r - d0, 0.72, 1));
          g.globalAlpha = 1;
          g.fillText(String(d0), x, cy - f * LH + 6);
          g.fillText(String((d0 + 1) % 10), x, cy + (1 - f) * LH + 6);
        }
        g.restore();
      }
      const grad = g.createLinearGradient(0, 0, 0, h); // soft top/bottom edges of the reel window
      grad.addColorStop(0, 'rgba(0,0,0,1)'); grad.addColorStop(0.18, 'rgba(0,0,0,0)'); grad.addColorStop(0.82, 'rgba(0,0,0,0)'); grad.addColorStop(1, 'rgba(0,0,0,1)');
      g.globalCompositeOperation = 'destination-out'; g.globalAlpha = 1; g.fillStyle = grad; g.fillRect(0, 0, w, h);
      g.globalCompositeOperation = 'source-over';
    });
    odo.position.set(0, 120, 0);
    const reelRules = segments(4, { additive: false });
    reelRules.begin(); for (const y of [56, 184]) reelRules.push(-420, y, 470, y, ink, 1); reelRules.end();
    reelRules.material.opacity = 0.12;

    // the bar: track, ticks with labels, fill, DEFAULT mark, sweep
    const track = line([[BX0, BY], [BX1, BY]], { opacity: 0.3 });
    const tickSegs = segments(22, { additive: false });
    tickSegs.begin();
    for (let k = 0; k <= 20; k++) { const x = lerp(BX0, BX1, k / 20); tickSegs.push(x, BY - 9, x, BY - (k % 5 === 0 ? 19 : 13), ink, k % 5 === 0 ? 1 : 0.6); }
    tickSegs.end(); tickSegs.material.opacity = 0.35;
    const tickLabs = [0, 60, 120, 180, 240].map((v, k) => { const m = text(`${v}`, { size: 10, font: FONT.mono, weight: 400, opacity: 0.4, align: 'center' }); m.position.set(lerp(BX0, BX1, k / 4), BY - 30, 0); return m; });
    const bar = rect(1, 10, C.amber); bar.geometry.translate(0.5, 0, 0); bar.position.set(BX0, BY, 0);
    const barGlow = rect(1, 26, C.amber, 0.12); barGlow.geometry.translate(0.5, 0, 0); barGlow.position.set(BX0, BY, 0);
    const mx = lerp(BX0, BX1, MARK);
    const markLine = line([[mx, BY - 22], [mx, BY + 34]], { opacity: 0.8 });
    const box = rect(116, 28, C.red, 0); box.position.set(mx, BY + 50, 0);
    const boxLine = line([[-58, -14], [58, -14], [58, 14], [-58, 14]], { closed: true, opacity: 0.7 }); boxLine.position.copy(box.position);
    const dflt = text('DEFAULT', { size: 15, font: FONT.mono, weight: 700, tracking: 4, align: 'center' }); dflt.position.set(mx + 2, BY + 50, 0);
    const ratioLab = text('DEBT / GDP', { size: 12, font: FONT.mono, weight: 400, tracking: 3, opacity: 0.55 }); ratioLab.position.set(BX0, BY + 28, 0);
    const readout = text('000 %', { size: 14, font: FONT.mono, weight: 700, align: 'center', fit: '000 %' });
    const flash = rect(1, 44, C.red, 0); flash.geometry.translate(0.5, 0, 0); flash.position.set(BX0, BY, 0);
    const sweep = segments(4, { additive: true });

    S.scene.add(label, odo, reelRules, track, tickSegs, ...tickLabs, barGlow, flash, bar, markLine, box, boxLine, dflt, ratioLab, readout, sweep);

    // the stat row (screen-locked): 10Y yield with step sparkline, FX reserves, sovereign rating
    const statRule = line([[-560, -96], [560, -96]], { opacity: 0.12 });
    const ylab = text('10Y  BOND YIELD', { size: 12, font: FONT.mono, weight: 400, tracking: 4, opacity: 0.6 }); ylab.position.set(-560, -116, 0);
    const yval = text(pct(31.2), { size: 64, font: FONT.cond, weight: 700, tracking: -0.5, fit: pct(88.8) }); yval.position.set(-562, -160, 0);
    const arrow = text('▲', { size: 20, font: FONT.sans, weight: 700 }); arrow.position.set(-372, -152, 0);
    const spark = segments(50, { additive: false });
    const rlab = text('FX RESERVES', { size: 12, font: FONT.mono, weight: 400, tracking: 4, opacity: 0.6 }); rlab.position.set(-150, -116, 0);
    const rval = text('USD 2.80 BN', { size: 40, font: FONT.cond, weight: 700, fit: 'USD 0.00 BN' }); rval.position.set(-152, -154, 0);
    const rbar = rect(1, 3, C.ink); rbar.geometry.translate(0.5, 0, 0); rbar.position.set(-150, -184, 0);
    const glab = text('SOVEREIGN RATING', { size: 12, font: FONT.mono, weight: 400, tracking: 4, opacity: 0.6, align: 'right' }); glab.position.set(560, -116, 0);
    const gval = text('B' + MINUS, { size: 64, font: FONT.cond, weight: 700, align: 'right', fit: 'CCC' }); gval.position.set(562, -160, 0); gval.renderOrder = 1;
    const gbox = rect(120, 64, C.red, 0); gbox.position.set(500, -160, 0);
    const gprev = text('', { size: 13, font: FONT.mono, weight: 400, tracking: 2, opacity: 0.45, align: 'right', fit: 'WAS CCC' }); gprev.position.set(560, -198, 0);
    S.hud.add(statRule, ylab, yval, arrow, spark, rlab, rval, rbar, glab, gbox, gval, gprev);

    return (s, i, t) => {
      const crossed = s >= TD, sinceD = S.since(TD), sinceG = S.since(TG), sinceL = S.since(TL);
      const hot = crossed ? Math.exp(-sinceD * 3) : 0;
      ac.copy(crossed ? red : amber);
      dust.update(t);

      lt.offset.y = -t * 0.018;
      ledger.material.color.copy(ink).lerp(red, crossed ? 0.35 + 0.65 * hot : 0);
      ledger.material.opacity = 0.11 + 0.12 * hot;

      // dial hand: accelerating sweep with trail
      const ang = (k) => Math.PI / 2 - Math.PI * 2 * (0.07 * k + 0.07 * k * k);
      hand.begin();
      for (let k = 0; k < 12; k++) {
        const a = ang(s - k * 0.016);
        hand.push(0, 0, Math.cos(a) * 336, Math.sin(a) * 336, crossed ? red : ink, (k === 0 ? 0.55 : 0.2) * (1 - k / 12));
      }
      hand.end();
      ticks.material.color.setRGB(1, 1, 1).lerp(red, hot);
      ticks.material.opacity = 0.16 + 0.3 * hot;
      dial.rotation.z = 0.04 * Math.sin(t * 0.4);

      label.typeOn(seg(s, 0.2, 0.8)); label.material.opacity = E.out(seg(s, 0.2, 0.5));
      odo.redraw(s, i);
      odo.material.opacity = E.out(seg(s, 0.35, 0.9));
      odo.material.color.copy(ink).lerp(red, crossed ? 0.35 * Math.exp(-sinceD * 2) + 0.12 : 0).multiplyScalar(sinceL >= 0 ? 1 + 0.6 * Math.exp(-sinceL * 5) : 1);
      odo.scale.setScalar(sinceL >= 0 ? lerp(1.05, 1, E.outExpo(seg(sinceL, 0, 0.3))) : 1);
      reelRules.material.opacity = 0.12 * E.out(seg(s, 0.4, 1)) + (sinceL >= 0 ? 0.3 * Math.exp(-sinceL * 4) : 0);

      const p = fill(s), draw = E.inOut(seg(s, 0.4, 1.2));
      track.scale.x = draw; tickSegs.material.opacity = 0.35 * draw;
      for (const m of tickLabs) m.material.opacity = 0.4 * draw;
      bar.scale.x = barGlow.scale.x = Math.max(0.001, p * (BX1 - BX0));
      bar.material.color.copy(ac).multiplyScalar(1 + 1.4 * (crossed ? Math.exp(-sinceD * 5) : 0));
      barGlow.material.color.copy(ac); barGlow.material.opacity = 0.1 + 0.25 * hot;
      flash.scale.x = (BX1 - BX0) * p; flash.material.opacity = crossed ? 0.35 * Math.exp(-sinceD * 6) : 0;
      readout.set(`${Math.round(p * 240)}${NNBSP}%`);
      readout.position.set(BX0 + p * (BX1 - BX0), BY - 46, 0);
      readout.material.color.copy(ac); readout.material.opacity = seg(s, 0.6, 0.9);
      ratioLab.material.opacity = 0.55 * seg(s, 0.6, 1.0);

      // DEFAULT: the box fills red, a red sweep runs out from the mark to both frame edges
      box.material.opacity = crossed ? E.out(seg(sinceD, 0, 0.25)) : 0;
      boxLine.material.color.copy(crossed ? red : ink);
      dflt.material.color.copy(crossed ? col(C.bg) : ink);
      markLine.material.color.copy(crossed ? red : ink);
      for (const o of [markLine, boxLine, dflt]) o.material.opacity = (o === markLine ? 0.8 : 1) * seg(s, 0.9, 1.3);
      box.scale.setScalar(crossed ? lerp(1.3, 1, E.outExpo(seg(sinceD, 0, 0.3))) : 1);
      sweep.begin();
      if (crossed) {
        const k = E.outExpo(seg(sinceD, 0, 0.5)), a = 1.6 * Math.exp(-sinceD * 2.5);
        for (const y of [BY + 14, BY - 14]) { sweep.push(mx, y, mx - 1300 * k, y, red, a, red, 0); sweep.push(mx, y, mx + 1300 * k, y, red, a, red, 0); }
      }
      sweep.end();

      // yield ticks
      let k = -1;
      while (k + 1 < TICKS.length && TICKS[k + 1].s <= s) k++;
      const yv = k < 0 ? TICKS[0].v : TICKS[k].v, since = k < 0 ? 9 : s - TICKS[k].s;
      yval.set(pct(yv));
      const pop = Math.exp(-since * 9);
      yval.material.color.copy(ac).lerp(ink, 0.25 * (1 - pop));
      yval.material.opacity = E.out(seg(s, 0.7, 1.1));
      ylab.material.opacity = 0.6 * E.out(seg(s, 0.6, 1.0));
      arrow.material.color.copy(ac); arrow.material.opacity = k >= 0 ? 0.4 + 0.6 * pop : 0;
      arrow.position.set(-560 + yval.textWidth + 12, -152 + 4 * pop, 0);
      spark.begin();
      const sx = (j) => -340 + j * 7.5, sy = (v) => -186 + (v - 8.4) * 2.4;
      for (let j = 1; j <= k; j++) {
        spark.push(sx(j - 1), sy(TICKS[j - 1].v), sx(j), sy(TICKS[j - 1].v), ac, 0.9);
        spark.push(sx(j), sy(TICKS[j - 1].v), sx(j), sy(TICKS[j].v), ac, 0.9);
      }
      spark.end();

      // FX reserves drain
      const res = lerp(2.8, 0.31, E.inOut(seg(s, 0.8, 4.6)));
      rval.set(`USD ${res.toFixed(2)} BN`);
      rval.material.opacity = E.out(seg(s, 0.8, 1.2)) * (res < 0.6 && hash(Math.floor(t * 8), 3) < 0.25 ? 0.4 : 1);
      rval.material.color.copy(res < 0.6 ? red : ink);
      rlab.material.opacity = 0.6 * E.out(seg(s, 0.7, 1.1));
      rbar.scale.x = Math.max(0.5, 300 * res / 2.8); rbar.material.opacity = 0.5 * E.out(seg(s, 0.8, 1.2));
      rbar.material.color.copy(res < 0.6 ? red : ink);

      // rating: B- -> CCC at the downgrade -> D at DEFAULT (red box)
      const rating = crossed ? 'D' : s >= TG ? 'CCC' : 'B' + MINUS;
      gval.set(rating);
      const flip = crossed ? sinceD : sinceG;
      gval.scale.setScalar(s >= TG ? lerp(1.25, 1, E.outExpo(seg(flip, 0, 0.3))) : 1);
      gval.material.color.copy(crossed ? col(C.bg) : s >= TG ? amber : ink).multiplyScalar(s >= TG && !crossed ? 1 + Math.exp(-sinceG * 6) : 1);
      gbox.material.opacity = crossed ? E.out(seg(sinceD, 0, 0.12)) : 0;
      gbox.scale.set((gval.textWidth + 30) / 120, 1, 1); gbox.position.x = 562 - gval.textWidth / 2;
      gprev.set(crossed ? 'WAS CCC' : s >= TG ? 'WAS B' + MINUS : '');
      for (const o of [gval]) o.material.opacity = E.out(seg(s, 0.8, 1.2));
      glab.material.opacity = 0.6 * E.out(seg(s, 0.7, 1.1));
      statRule.material.opacity = 0.12 * E.out(seg(s, 0.5, 1.2));

      const glitch = S.at(TD, 2) ? 1 : S.at(TG) ? 0.4 : S.at(TL) ? 0.3 : S.at(1.3) ? 0.3 : 0;
      return { glitch, shake: (crossed ? 12 * Math.exp(-sinceD * 8) : 0) + (sinceG >= 0 ? 4 * Math.exp(-sinceG * 10) : 0) };
    };
  },
};
