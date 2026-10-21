// Page side of render.mjs. ?scene=<name>[&dur=<seconds>][&res=1080]: renders frame i at t = i/30 and encodes VP8 WebM (window.encodeClip).
// ?sheet: decodes finished clips with a <video> element and draws the contact sheet (window.contactSheet).
import { Muxer, ArrayBufferTarget } from 'webm-muxer';
import { createStage, W, H, PX, FPS } from './look.js';

const params = new URLSearchParams(location.search);
const canvas = document.getElementById('c');

const toB64 = (buf) => new Promise((res) => {
  const r = new FileReader();
  r.onload = () => res(String(r.result).split(',')[1]);
  r.readAsDataURL(new Blob([buf]));
});

if (params.has('scene')) {
  const name = params.get('scene');
  await document.fonts.load('700 40px "Bahnschrift Condensed"');
  const mod = (await import(`./scenes/${name}.js`)).default;
  const D = Number(params.get('dur')) || mod.duration;
  canvas.width = W * PX; canvas.height = H * PX;
  const stage = createStage(canvas, mod, D);
  stage.update = mod.build(stage);
  const frames = Math.round(D * FPS);
  window.renderFrame = (i) => stage.frame(i); // preview / debugging: draw frame i on the canvas

  // beats in seconds of the rendered clip (the frame on which each one shows), plus a round-trip check of the time warp
  window.clipInfo = () => {
    const problems = [];
    for (let s = 0; s <= mod.duration; s += 0.01) {
      const back = stage.warp.story(stage.warp.real(s));
      if (Math.abs(back - s) > 1e-3) { problems.push(`time warp round trip off at story ${s.toFixed(2)} s`); break; }
    }
    const beats = stage.beats.slice().sort((a, b) => a.s - b.s).map(b => ({ t: +(stage.frameOf(b.s) / FPS).toFixed(3), name: b.name }));
    for (const b of beats) if (b.t < 0.45 || b.t >= D - 0.4) problems.push(`beat ${b.name} at ${b.t} s is inside a fade`);
    return { duration: +(frames / FPS).toFixed(3), frames, defaultDuration: mod.duration, warpScale: +stage.warp.scale.toFixed(3), beats, problems };
  };

  window.encodeClip = async () => {
    const muxer = new Muxer({ target: new ArrayBufferTarget(), video: { codec: 'V_VP8', width: W * PX, height: H * PX, frameRate: FPS } });
    let codec = null, failure = null;
    const enc = new VideoEncoder({
      output: (chunk, meta) => { if (meta?.decoderConfig) codec = meta.decoderConfig.codec; muxer.addVideoChunk(chunk, meta); },
      error: (e) => { failure = String(e); },
    });
    enc.configure({ codec: 'vp8', width: W * PX, height: H * PX, bitrate: Math.round(3_000_000 * PX * PX), framerate: FPS, latencyMode: 'quality' });
    for (let i = 0; i < frames; i++) {
      stage.frame(i);
      const vf = new VideoFrame(canvas, { timestamp: Math.round(i * 1e6 / FPS), duration: Math.round(1e6 / FPS) });
      enc.encode(vf, { keyFrame: i % 60 === 0 });
      vf.close();
      if (enc.encodeQueueSize > 6) await new Promise(r => enc.addEventListener('dequeue', r, { once: true }));
      if (failure) break;
    }
    await enc.flush();
    enc.close();
    if (failure) throw new Error(failure);
    muxer.finalize();
    return { b64: await toB64(muxer.target.buffer), codec, frames, width: W * PX, height: H * PX };
  };
  window.ready = true;
}

if (params.has('sheet')) {
  window.contactSheet = async (clips, fullFrames = false) => { // clips: [{ name, file, beats? }]; times come from the decoded duration
    const TW = 400, TH = 225, LW = 210, GAP = 10, ROW = TH + 58, TOP = 56;
    const PCT = [0.15, 0.40, 0.65, 0.90];
    const sheet = document.createElement('canvas');
    sheet.width = LW + PCT.length * (TW + GAP) + GAP;
    sheet.height = TOP + clips.length * ROW + GAP;
    const g = sheet.getContext('2d');
    g.fillStyle = '#111'; g.fillRect(0, 0, sheet.width, sheet.height);
    g.fillStyle = '#e6e1d3'; g.font = '700 22px Consolas';
    g.fillText('MOTION SCREENS - contact sheet (decoded from the encoded WebM files; ticks = beats.json)', GAP + 4, 34);
    const report = [], frames = [];
    for (const [row, c] of clips.entries()) {
      const blob = await (await fetch(c.file)).blob();
      const v = document.createElement('video');
      v.muted = true; v.preload = 'auto'; v.src = URL.createObjectURL(blob);
      await new Promise((res, rej) => { v.onloadeddata = res; v.onerror = () => rej(new Error(`${c.name}: video element could not decode`)); });
      const y = TOP + row * ROW;
      g.fillStyle = '#e6e1d3'; g.font = '700 20px Consolas'; g.fillText(c.name, GAP + 4, y + 30);
      g.font = '14px Consolas'; g.fillStyle = '#9a968c';
      g.fillText(`${v.duration.toFixed(2)} s decoded`, GAP + 4, y + 54);
      g.fillText(`${v.videoWidth}x${v.videoHeight}`, GAP + 4, y + 74);
      g.fillText(`${(blob.size / 1024).toFixed(0)} KiB`, GAP + 4, y + 94);
      for (const [k, p] of PCT.entries()) {
        const t = p * v.duration;
        v.currentTime = t;
        await new Promise(res => { v.onseeked = res; });
        const x = LW + k * (TW + GAP);
        g.drawImage(v, x, y, TW, TH);
        g.strokeStyle = 'rgba(230,225,211,0.25)'; g.setLineDash([4, 4]); // the game's caption zone (bottom 12 %)
        g.beginPath(); g.moveTo(x, y + TH * 0.88 + 0.5); g.lineTo(x + TW, y + TH * 0.88 + 0.5); g.stroke(); g.setLineDash([]);
        if (fullFrames) {
          const f = document.createElement('canvas');
          f.width = v.videoWidth; f.height = v.videoHeight;
          f.getContext('2d').drawImage(v, 0, 0);
          frames.push({ file: `${c.name}-${Math.round(p * 100)}.png`, png: f.toDataURL('image/png').split(',')[1] });
        }
        g.fillStyle = '#9a968c'; g.font = '13px Consolas';
        g.fillText(`${Math.round(p * 100)} %  t=${t.toFixed(2)} s`, x, y + TH + 16);
      }
      // beat timeline across the row: one tick per beat, the 4 sample times as hollow marks
      const x0 = LW, x1 = LW + PCT.length * (TW + GAP) - GAP, ty = y + TH + 28, tx = (t) => x0 + (x1 - x0) * t / v.duration;
      g.fillStyle = 'rgba(230,225,211,0.18)'; g.fillRect(x0, ty, x1 - x0, 1);
      for (const p of PCT) { g.strokeStyle = '#9a968c'; g.strokeRect(tx(p * v.duration) - 2.5, ty - 2.5, 5, 5); }
      g.font = '11px Consolas';
      let lastLabel = -1e9, lane = 1;
      for (const b of c.beats ?? []) {
        const bx = tx(b.t);
        g.fillStyle = '#e0453a'; g.fillRect(bx - 1, ty - 5, 2, 11);
        if (bx - lastLabel < 28) continue; // dense runs (stamps) keep their ticks, not every label
        lane = 1 - lane; lastLabel = bx;
        g.fillStyle = '#c9c4b6'; g.fillText(b.name, bx + 3, ty + 15 + lane * 11);
      }
      const luma = async (t) => { // mean 0..255 brightness of the decoded frame at t
        v.currentTime = t;
        await new Promise(res => { v.onseeked = res; });
        const s = document.createElement('canvas'); s.width = 64; s.height = 36;
        const sg = s.getContext('2d'); sg.drawImage(v, 0, 0, 64, 36);
        const d = sg.getImageData(0, 0, 64, 36).data;
        let sum = 0; for (let k = 0; k < d.length; k += 4) sum += (d[k] + d[k + 1] + d[k + 2]) / 3;
        return sum / (d.length / 4);
      };
      const r = { name: c.name, duration: v.duration, width: v.videoWidth, height: v.videoHeight,
        firstLuma: await luma(0), lastLuma: await luma(Math.max(0, v.duration - 0.005)) };
      g.fillStyle = '#9a968c'; g.font = '14px Consolas';
      g.fillText(`first/last luma`, GAP + 4, y + 124);
      g.fillText(`${r.firstLuma.toFixed(1)} / ${r.lastLuma.toFixed(1)} of 255`, GAP + 4, y + 144);
      g.fillText(`${(c.beats ?? []).length} beats`, GAP + 4, y + 168);
      report.push(r);
    }
    return { png: sheet.toDataURL('image/png').split(',')[1], report, frames };
  };
  window.ready = true;
}
