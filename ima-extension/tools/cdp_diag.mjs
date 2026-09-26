// 完全复刻 C# v1.1.5 的原始 CDP 命令序列，诊断空白 PDF 问题
import { spawn } from 'child_process';
import { existsSync, readFileSync, writeFileSync, mkdtempSync, rmSync } from 'fs';
import { tmpdir } from 'os';
import { join } from 'path';
import { createRequire } from 'module';
const require = createRequire('C:/Users/Administrator/.workbuddy/binaries/node/workspace/node_modules/puppeteer-core/package.json');
const WebSocket = require('ws');

const url = process.argv[2] || 'https://example.com/';
const outPdf = process.argv[3] || 'H:/WorkBuddy/工具开发/ima资源下载/build/pdftest/diag.pdf';
const edge = ['C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
              'C:/Program Files/Microsoft/Edge/Application/msedge.exe'].find(existsSync);
const profile = mkdtempSync(join(tmpdir(), 'ima_pdf_'));
const PORT = 9337;

const proc = spawn(edge, [
  '--headless=new', '--disable-gpu', '--no-first-run', '--disable-extensions',
  '--disable-background-networking', '--no-default-browser-check',
  `--remote-debugging-port=${PORT}`, `--user-data-dir=${profile}`, 'about:blank'
], { stdio: 'ignore' });

async function main() {
  try {
  // 等 DevTools 就绪
  let ready = false;
  for (let i = 0; i < 60; i++) {
    try {
      const r = await fetch(`http://127.0.0.1:${PORT}/json/version`);
      if (r.ok) { ready = true; break; }
    } catch {}
    await new Promise(r => setTimeout(r, 250));
  }
  if (!ready) throw new Error('DevTools 未就绪');
  console.log('DevTools 就绪');

  // PUT /json/new 建空白页（query 直接是 URL）
  const resp = await fetch(`http://127.0.0.1:${PORT}/json/new?about:blank`, { method: 'PUT' });
  const target = await resp.json();
  console.log('target id:', target.id, '| type:', target.type, '| url:', (target.url || '').slice(0, 60));

  const ws = new WebSocket(target.webSocketDebuggerUrl);
  await new Promise((res, rej) => { ws.on('open', res); ws.on('error', rej); });

  let msgId = 0;
  const pending = new Map();
  ws.on('message', data => {
    const m = JSON.parse(data.toString());
    if (m.id && pending.has(m.id)) { pending.get(m.id)(m); pending.delete(m.id); }
  });
  const send = (method, params = {}) => new Promise((res, rej) => {
    const id = ++msgId;
    pending.set(id, m => m.error ? rej(new Error(method + ': ' + JSON.stringify(m.error).slice(0, 120))) : res(m.result));
    ws.send(JSON.stringify({ id, method, params }));
  });
  const waitEvent = (method, timeoutMs) => new Promise(res => {
    const h = data => { const m = JSON.parse(data.toString()); if (m.method === method) { ws.off('message', h); res(m.params); } };
    ws.on('message', h);
    setTimeout(() => { ws.off('message', h); res(null); }, timeoutMs);
  });

  await send('Page.enable');

  // 连接就绪后再导航（时序可控）
  await send('Page.navigate', { url });

  // 轮询 readyState + URL（绕开 about:blank 假完成与导航期上下文销毁）
  let state = '', curUrl = '';
  for (let i = 0; i < 80; i++) {
    try {
      const r = await send('Runtime.evaluate', { expression: 'JSON.stringify({s: document.readyState, u: location.href})', returnByValue: true });
      const o = JSON.parse(r.result?.value || '{}');
      state = o.s || ''; curUrl = o.u || '';
      if (state === 'complete' && !curUrl.includes('about:blank')) break;
    } catch (e) { state = 'CTX_DESTROYED'; }
    await new Promise(r => setTimeout(r, 500));
  }
  console.log('readyState:', state, '| URL:', curUrl.slice(0, 70));

  // 滚动 + 等图片
  const ev = await send('Runtime.evaluate', {
    expression: `(async () => {
      const sleep = ms => new Promise(r => setTimeout(r, ms));
      try {
        const H = () => Math.max(document.body ? document.body.scrollHeight : 0, document.documentElement ? document.documentElement.scrollHeight : 0);
        for (let y = 0; y < H() + 700; y += 700) { window.scrollTo(0, y); await sleep(150); }
        window.scrollTo(0, 0); await sleep(300);
        const t0 = Date.now();
        while (Date.now() - t0 < 30000) {
          const imgs = Array.from(document.images || []);
          const pending = imgs.filter(i => (!i.complete && i.src && !i.src.startsWith('data:')) || (i.dataset && i.dataset.src && i.src !== i.dataset.src));
          if (imgs.length === 0 || pending.length === 0) break;
          await sleep(400);
        }
        await sleep(400);
        return JSON.stringify({ imgs: document.images.length, title: document.title.slice(0, 40), bodyLen: (document.body.innerText || '').length });
      } catch (e) { return 'ERR ' + e.message; }
    })()`,
    awaitPromise: true, returnByValue: true
  });
  console.log('页面信息:', ev.result?.value);

  // 打印
  const pdf = await send('Page.printToPDF', { printBackground: true });
  writeFileSync(outPdf, Buffer.from(pdf.data, 'base64'));
  console.log('PDF 已生成:', outPdf, pdf.data.length, 'base64 chars');
} finally {
  proc.kill();
  try { rmSync(profile, { recursive: true, force: true }); } catch {}
}
}
main().then(() => process.exit(0)).catch(e => { console.error('FAIL:', e.message); process.exit(1); });
