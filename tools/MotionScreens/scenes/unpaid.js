// 04 UNPAID: a wall of service tags. The 24 in front flip one after another to a red UNPAID stamp; WEEKS WITHOUT PAY lands on 14;
// then the rest of the wall, out to the edges of the frame, flips in one ripple: everyone.
import { THREE, C, FONT, E, seg, lerp, rng, hash, col, text, canvasPlane, line, motes, scrim } from '../look.js';

const COLS = 12, ROWS = 8, TW = 140, TH = 78, GX = 20, GY = 18, FLIP = 0.45;
const HC = [3, 8], HR = [2, 5];                       // the hero block (inclusive column / row ranges)
const N = 24;
const start = (k) => 0.9 + 2.9 * Math.pow(k / (N - 1), 0.62);
const TF = start(N - 1) + FLIP + 0.05;                // the last stamp lands: 14
const TR = TF + 0.32, RIPPLE = 0.032, FLIP2 = 0.32;   // the wall ripple (per pitch of distance from the hero block)
const RANKS = ['PTE', 'CPL', 'SGT', 'PTE', 'LCPL', 'SGT', 'PTE', 'CPL', 'WO2', 'PTE', 'LT', 'CPL'];
const UNITS = ['1 INF BDE', '3 MAR CDO', 'NAVY / FFG', 'AIR / 2 SQN', '6 ARMD REGT', 'MP CORPS', 'SIG REGT', '12 ENGR'];

function tagShape(g, w, h) {
  const r = 10;
  g.beginPath();
  g.moveTo(r, 1); g.lineTo(w - r, 1); g.quadraticCurveTo(w - 1, 1, w - 1, r); g.lineTo(w - 1, h - r);
  g.quadraticCurveTo(w - 1, h - 1, w - r, h - 1); g.lineTo(r, h - 1); g.quadraticCurveTo(1, h - 1, 1, h - r); g.lineTo(1, r); g.quadraticCurveTo(1, 1, r, 1);
  g.closePath();
}

function drawTag(g, w, h, R, k, back) {
  const rk = RANKS[k % RANKS.length], no = `BR ${40 + Math.floor(R() * 50)}-${1000 + Math.floor(R() * 8999)}`, unit = UNITS[Math.floor(R() * UNITS.length)];
  const bars = Array.from({ length: 34 }, () => 1 + Math.floor(R() * 3));
  tagShape(g, w, h);
  g.fillStyle = '#0b1119'; g.fill();
  g.lineWidth = 1.2; g.strokeStyle = back ? 'rgba(224,69,58,0.75)' : 'rgba(230,225,211,0.7)'; g.stroke();
  g.beginPath(); g.arc(14, h / 2, 4.5, 0, Math.PI * 2); g.stroke();
  const a = back ? 0.3 : 1;
  g.fillStyle = `rgba(230,225,211,${a})`; g.strokeStyle = `rgba(230,225,211,${a})`;
  g.font = `700 22px ${FONT.cond}`; g.textBaseline = 'alphabetic'; g.fillText(rk, 27, 27);
  g.font = `400 9.5px ${FONT.mono}`; g.fillText(no, 27, 40);
  g.font = `400 7.5px ${FONT.mono}`; g.globalAlpha = 0.6; g.fillText(unit, 27, 49); g.globalAlpha = 1;
  const ch = rk === 'SGT' || rk === 'WO2' ? 3 : rk === 'CPL' || rk === 'LCPL' ? 2 : rk === 'LT' ? 0 : 1;
  g.lineWidth = 1.6;
  for (let c = 0; c < ch; c++) { g.beginPath(); g.moveTo(w - 34, 12 + c * 6); g.lineTo(w - 24, 18 + c * 6); g.lineTo(w - 14, 12 + c * 6); g.stroke(); }
  if (rk === 'LT') g.fillRect(w - 30, 13, 12, 4);
  let x = 27;
  for (const [j, bw] of bars.entries()) { if (j % 2 === 0) g.fillRect(x, 54, bw, 15); x += bw + 0.8; if (x > w - 14) break; }
}

export default {
  code: '04', title: 'UNPAID', duration: 6, bloom: 1.1,
  rigid: [[TF - 0.15, TR + 0.85]],
  camera: { push: 0.1, drift: [12, 7] },
  build(S) {
    const R = rng(404), red = col(C.red), ink = col(C.ink);
    const grid = new THREE.Group();
    grid.position.set(0, -14, 0);
    grid.rotation.set(-0.36, 0.05, 0.035);
    grid.scale.setScalar(0.78);
    S.scene.add(grid);
    const dust = motes(50, { seed: 41, alpha: 0.06 });
    S.scene.add(dust);

    // one shared stamp texture with a worn edge
    const SR = rng(7);
    const stampTex = canvasPlane(118, 40, (g, w, h) => {
      g.strokeStyle = '#fff'; g.fillStyle = '#fff';
      g.lineWidth = 2.4; g.strokeRect(3, 3, w - 6, h - 6);
      g.lineWidth = 1; g.strokeRect(7, 7, w - 14, h - 14);
      g.font = `700 25px ${FONT.cond}`; g.letterSpacing = '3px'; g.textAlign = 'center'; g.textBaseline = 'middle';
      g.fillText('UNPAID', w / 2 + 1, h / 2 + 2);
      g.globalCompositeOperation = 'destination-out';
      for (let s = 0; s < 70; s++) { g.globalAlpha = 0.3 + SR() * 0.7; g.fillRect(SR() * w, SR() * h, 0.6 + SR() * 2.2, 0.6 + SR() * 1.6); }
    }).material.map;
    const stampGeo = new THREE.PlaneGeometry(118, 40);
    const makeStamp = (rot) => {
      const m = new THREE.Mesh(stampGeo, new THREE.MeshBasicMaterial({ map: stampTex, color: C.red, transparent: true, depthTest: false, depthWrite: false }));
      m.position.set(8, -2, 0.5); m.rotation.z = rot; m.renderOrder = 2; m.frustumCulled = false;
      return m;
    };

    // the wall: hero tags have their own textures; the context tags share 8 variants and two materials
    const variants = Array.from({ length: 8 }, (_, v) => [false, true].map(back => canvasPlane(TW, TH, (g, w, h) => drawTag(g, w, h, rng(700 + v), v * 5 + 1, back))));
    const ctxMat = [new THREE.MeshBasicMaterial({ transparent: true, depthTest: false, depthWrite: false }), null];
    ctxMat[1] = ctxMat[0].clone();
    const cards = [], ctx = [];
    let hk = 0;
    const heroOrder = [];
    for (let r = 0; r < ROWS; r++) for (let c = 0; c < COLS; c++) {
      const hero = c >= HC[0] && c <= HC[1] && r >= HR[0] && r <= HR[1];
      let front, back;
      if (hero) {
        const k = hk++, seedF = 900 + k;
        front = canvasPlane(TW, TH, (g, w, h) => drawTag(g, w, h, rng(seedF), k, false));
        back = canvasPlane(TW, TH, (g, w, h) => drawTag(g, w, h, rng(seedF), k, true));
      } else {
        const v = Math.floor(R() * 8);
        front = new THREE.Mesh(variants[v][0].geometry, ctxMat[0].clone()); front.material.map = variants[v][0].material.map;
        back = new THREE.Mesh(variants[v][1].geometry, ctxMat[1].clone()); back.material.map = variants[v][1].material.map;
        front.frustumCulled = back.frustumCulled = false;
      }
      for (const m of [front, back]) m.material.side = THREE.FrontSide;
      back.rotation.x = Math.PI;
      const stamp = makeStamp((R() - 0.5) * 0.35);
      back.renderOrder = 1;
      back.add(stamp);
      const card = new THREE.Group();
      card.add(front, back);
      card.position.set((c - (COLS - 1) / 2) * (TW + GX), ((ROWS - 1) / 2 - r) * (TH + GY), 0);
      grid.add(card);
      const d = { card, stamp, front, back, hero, r, c };
      if (hero) heroOrder.push(d); else {
        const dc = Math.max(HC[0] - c, c - HC[1], 0), dr = Math.max(HR[0] - r, r - HR[1], 0);
        d.s = TR + Math.hypot(dc, dr * 1.4) * RIPPLE * 3 + R() * 0.05;
        d.dim = 0.42 * (r > HR[1] ? 0.7 : r < 1 ? 0.6 : 1) * (1 - 0.12 * Math.hypot(dc, dr));
        ctx.push(d);
      }
      cards.push(d);
    }
    heroOrder.forEach((d, k) => { d.s = start(k); S.beat(`stamp-${String(k + 1).padStart(2, '0')}`, d.s + FLIP); });
    S.beat('fourteen', TF);
    S.beat('ripple', TR);

    // type: the hero counter top left with its ledger lines, the unpaid headcount top right
    const lab = text('WEEKS WITHOUT PAY', { size: 13, font: FONT.mono, weight: 700, tracking: 6 });
    lab.position.set(-560, 258, 0);
    const num = text('14', { size: 150, font: FONT.cond, weight: 700, tracking: -2, fit: '00' });
    num.position.set(-566, 182, 0);
    const lines = ['LAST PAY RUN      06 MAR 2034', 'PAYROLL           BOSCALI DEFENCE FORCE', 'FUNDS AVAILABLE   USD 0.00'].map((s, k) => {
      const m = text(s, { size: 12, font: FONT.mono, weight: k === 2 ? 700 : 400, tracking: 1.5, opacity: 0.6 });
      m.position.set(-400, 222 - k * 19, 0); return m;
    });
    const rule = line([[-404, 238], [-60, 238]], { opacity: 0.2 });
    const hcLab = text('PERSONNEL UNPAID', { size: 12, font: FONT.mono, weight: 400, tracking: 4, opacity: 0.6, align: 'right' });
    hcLab.position.set(560, 258, 0);
    const hc = text('00 000', { size: 44, font: FONT.mono, weight: 700, align: 'right', fit: '00 000' });
    hc.position.set(560, 222, 0);
    const allUnits = text('ALL UNITS', { size: 14, font: FONT.mono, weight: 700, tracking: 6, color: C.red, align: 'right' });
    allUnits.position.set(560, 186, 0);
    S.hud.add(scrim(60), lab, num, rule, ...lines, hcLab, hc, allUnits);
    const fmtK = (v) => String(Math.round(v)).padStart(5, '0').replace(/(\d{2})(\d{3})$/, '$1 $2');

    return (s, i, t) => {
      dust.update(t);
      const intro = E.out(seg(s, 0.2, 0.9));
      let flipped = 0;
      for (const cd of cards) {
        const dur = cd.hero ? FLIP : FLIP2;
        const k = seg(s, cd.s, cd.s + dur), land = S.since(cd.s + dur);
        cd.card.rotation.x = Math.PI * E.inOut(k);
        cd.card.position.z = (cd.hero ? 46 : 26) * Math.sin(Math.PI * k) - (land > 0 ? 7 * Math.exp(-land * 9) * Math.sin(land * 40) : 0);
        const sk = seg(land, 0, 0.14);
        cd.stamp.scale.setScalar(land < 0 ? 1.7 : lerp(1.7, 1, E.outExpo(sk)));
        const a = cd.hero ? 1 : cd.dim;
        cd.stamp.material.opacity = land < 0 ? 0 : E.out(sk) * a;
        cd.stamp.material.color.copy(red).multiplyScalar(1 + 1.2 * Math.exp(-Math.max(0, land) * 5));
        cd.front.material.opacity = cd.back.material.opacity = intro * a;
        if (cd.hero && land >= 0) flipped++;
      }
      const sinceF = S.since(TF), sinceR = S.since(TR);
      grid.position.y = -14 - 16 * (1 - intro) - (sinceF >= 0 ? 6 * Math.exp(-sinceF * 7) : 0);

      const weeks = s >= TF ? 14 : 9 + Math.floor(4.999 * flipped / N);
      num.set(String(weeks));
      num.scale.setScalar(sinceF >= 0 ? lerp(1.3, 1, E.outExpo(seg(sinceF, 0, 0.3))) : 1);
      num.material.color.copy(s >= TF ? red : ink).lerp(red, flipped / N * 0.5).multiplyScalar(sinceF >= 0 ? 1 + 0.8 * Math.exp(-sinceF * 5) : 1);
      num.material.opacity = E.out(seg(s, 0.3, 0.8));
      lab.typeOn(seg(s, 0.15, 0.7)); lab.material.opacity = E.out(seg(s, 0.15, 0.4));
      lines.forEach((m, k) => {
        m.typeOn(seg(s, 0.5 + k * 0.25, 1.1 + k * 0.25));
        m.material.opacity = 0.6 * (k === 2 && s > 1 && hash(Math.floor(t * 5), 7) < 0.3 ? 0.25 : 1);
        if (k === 2) m.material.color.copy(red);
      });
      rule.material.opacity = 0.2 * E.out(seg(s, 0.4, 1.0));

      const heads = s < TR ? 38_400 * flipped / N : lerp(38_400, 41_260, E.out(seg(sinceR, 0, 0.7)));
      hc.set(fmtK(heads + (s < TF && flipped ? 40 * hash(i, 4) : 0)));
      hc.material.color.copy(flipped ? red : ink);
      hc.material.opacity = hcLab.material.opacity = E.out(seg(s, 0.4, 0.9));
      allUnits.visible = sinceR >= 0.25;
      allUnits.typeOn(seg(sinceR, 0.25, 0.7));

      const glitch = S.at(TF, 2) ? 1 : S.at(TR) ? 0.45 : S.at(2.1) ? 0.3 : 0;
      return { glitch, shake: (sinceF >= 0 ? 14 * Math.exp(-sinceF * 7) : 0) + (sinceR >= 0 ? 5 * Math.exp(-sinceR * 5) : 0) };
    };
  },
};
