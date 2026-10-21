// Renders the motion screens to out/tmo-screen-<name>.webm (VP8, 1280x720, 30 fps), out/beats.json and out/contact-sheet.png.
//   node render.mjs            all scenes at the lengths in durations.json, then beats.json and the contact sheet
//   node render.mjs riots      one scene (beats.json is merged, the sheet is rebuilt from every clip in out/)
//   node render.mjs --sheet    contact sheet only
//   --frames                   also write the sampled frames full size to out/frames/ (for review, not committed)
//   --res=1080                 render 1920x1080 into out/1080/ instead (no sheet, beats are the same)
// Drives the installed Microsoft Edge headless through puppeteer-core; frames are rendered one by one at t = i/30,
// so the output never depends on machine speed.
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import puppeteer from 'puppeteer-core';

const ROOT = path.dirname(fileURLToPath(import.meta.url));
const EDGE = process.env.EDGE_PATH || 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe';
const SCENES = ['markets', 'debt', 'blackout', 'unpaid', 'riots', 'signal', 'exodus'];
const fileOf = (name) => `tmo-screen-${name}.webm`;

const args = process.argv.slice(2);
const sheetOnly = args.includes('--sheet');
const res = Number(args.find(a => a.startsWith('--res='))?.slice(6) || 720);
if (![720, 1080].includes(res)) { console.error('--res takes 720 or 1080'); process.exit(2); }
const OUT = path.join(ROOT, 'out', res === 720 ? '' : String(res));
const picked = args.filter(a => !a.startsWith('--'));
for (const n of picked) if (!SCENES.includes(n)) { console.error(`unknown scene "${n}"; scenes: ${SCENES.join(', ')}`); process.exit(2); }
const names = sheetOnly ? [] : (picked.length ? picked : SCENES);

// target lengths: the narration decides them. A missing scene plays at its default (the scene module's duration).
const durations = JSON.parse(fs.readFileSync(path.join(ROOT, 'durations.json'), 'utf8'));
for (const [n, d] of Object.entries(durations)) {
  if (!SCENES.includes(n)) { console.error(`durations.json: unknown scene "${n}"`); process.exit(2); }
  if (!(typeof d === 'number' && d >= 2.5 && d <= 30)) { console.error(`durations.json: ${n} = ${d}; give seconds between 2.5 and 30`); process.exit(2); }
}

// static server: ES modules and blob-free fetches need http://localhost (WebCodecs also needs a secure context)
const MIME = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.webm': 'video/webm', '.png': 'image/png', '.json': 'application/json' };
const server = http.createServer((req, res) => {
  const rel = decodeURIComponent(new URL(req.url, 'http://x').pathname).replace(/^\/+/, '');
  const file = path.resolve(ROOT, rel || 'index.html');
  if (!file.startsWith(ROOT) || !fs.existsSync(file) || !fs.statSync(file).isFile()) { res.writeHead(404); res.end(); return; }
  res.writeHead(200, { 'content-type': MIME[path.extname(file)] || 'application/octet-stream' });
  fs.createReadStream(file).pipe(res);
}).listen(0, '127.0.0.1');
await new Promise(r => server.once('listening', r));
const BASE = `http://127.0.0.1:${server.address().port}`;

const GPU = ['--use-angle=d3d11', '--ignore-gpu-blocklist', '--enable-gpu'];
const SOFT = ['--use-angle=swiftshader', '--enable-unsafe-swiftshader'];
async function launch(flags) {
  const browser = await puppeteer.launch({ executablePath: EDGE, headless: true, protocolTimeout: 900_000,
    args: [...flags, '--hide-scrollbars', '--mute-audio', '--force-prefers-no-reduced-motion'], defaultViewport: { width: 1280, height: 720 } });
  const p = await browser.newPage();
  await p.goto(`${BASE}/index.html`);
  const gl = await p.evaluate(() => !!document.createElement('canvas').getContext('webgl2'));
  await p.close();
  return gl ? browser : (await browser.close(), null);
}
let browser = await launch(GPU), mode = 'd3d11 GPU';
if (!browser) { browser = await launch(SOFT); mode = 'SwiftShader'; }
if (!browser) throw new Error('no WebGL2 in headless Edge, neither GPU nor SwiftShader');
console.log(`Edge headless, WebGL via ${mode}`);

async function openPage(query) {
  const page = await browser.newPage();
  const errors = [];
  page.on('pageerror', e => errors.push(String(e)));
  page.on('console', m => { if (m.type() === 'error' || m.type() === 'warn') errors.push(`${m.type()}: ${m.text()}`); });
  await page.goto(`${BASE}/index.html?${query}`);
  try { await page.waitForFunction('window.ready === true', { timeout: 30_000 }); }
  catch { throw new Error(`page ?${query} never became ready:\n${errors.join('\n')}`); }
  return { page, errors };
}

fs.mkdirSync(OUT, { recursive: true });
const beatsFile = path.join(ROOT, 'out', 'beats.json');
const beats = fs.existsSync(beatsFile) ? JSON.parse(fs.readFileSync(beatsFile, 'utf8')) : {};
let failed = false;
for (const name of names) {
  const t0 = Date.now();
  const dur = durations[name];
  const { page, errors } = await openPage(`scene=${name}${dur ? `&dur=${dur}` : ''}${res === 720 ? '' : `&res=${res}`}`);
  const info = await page.evaluate(() => window.clipInfo());
  const r = await page.evaluate(() => window.encodeClip());
  await page.close();
  const bytes = Buffer.from(r.b64, 'base64');
  const ebml = bytes.subarray(0, 4).toString('hex') === '1a45dfa3';
  const ok = ebml && r.codec === 'vp8' && r.height === res && !info.problems.length;
  failed ||= !ok;
  fs.writeFileSync(path.join(OUT, fileOf(name)), bytes);
  if (res === 720) beats[name] = { duration: info.duration, beats: info.beats };
  console.log(`${ok ? 'ok ' : 'BAD'} ${fileOf(name).padEnd(28)} ${(bytes.length / 1024).toFixed(0).padStart(5)} KiB  ${r.width}x${r.height}  ${r.frames} frames = ${info.duration.toFixed(2)} s`
    + ` (default ${info.defaultDuration} s, elastic x${info.warpScale})  codec=${r.codec}  EBML=${ebml}  ${info.beats.length} beats  (${((Date.now() - t0) / 1000).toFixed(1)} s)`);
  for (const p of info.problems) console.log(`    PROBLEM: ${p}`);
  for (const e of errors) console.log(`    page: ${e}`);
}
if (res === 720 && names.length) {
  const ordered = Object.fromEntries(SCENES.filter(n => beats[n]).map(n => [n, beats[n]]));
  fs.writeFileSync(beatsFile, JSON.stringify(ordered, null, 2) + '\n');
  console.log(`beats: ${beatsFile}`);
}

const clips = res !== 720 ? [] : SCENES.filter(n => fs.existsSync(path.join(OUT, fileOf(n)))).map(n => ({ name: n, file: `out/${fileOf(n)}`, beats: beats[n]?.beats }));
if (clips.length) {
  const { page, errors } = await openPage('sheet');
  const { png, report, frames } = await page.evaluate((c, f) => window.contactSheet(c, f), clips, args.includes('--frames'));
  await page.close();
  fs.writeFileSync(path.join(OUT, 'contact-sheet.png'), Buffer.from(png, 'base64'));
  if (frames.length) fs.mkdirSync(path.join(OUT, 'frames'), { recursive: true });
  for (const f of frames) fs.writeFileSync(path.join(OUT, 'frames', f.file), Buffer.from(f.png, 'base64'));
  for (const c of report) {
    const want = beats[c.name]?.duration;
    const off = want !== undefined && Math.abs(c.duration - want) > 0.05;
    failed ||= off;
    console.log(`decoded ${c.name.padEnd(9)} ${c.duration.toFixed(2)} s${off ? ` (BAD: rendered ${want} s)` : ''}  ${c.width}x${c.height}  first/last frame mean luma ${c.firstLuma.toFixed(1)} / ${c.lastLuma.toFixed(1)}`);
  }
  for (const e of errors) console.log(`    sheet page: ${e}`);
  console.log(`contact sheet: ${path.join(OUT, 'contact-sheet.png')}`);
}

await browser.close();
server.close();
process.exit(failed ? 1 : 0);
