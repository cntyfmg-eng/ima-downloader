// 验证 CDP 打印流程：滚动触发懒加载 → 等图片完成 → printToPDF（与 C# v1.1.5 实现等价）
import { existsSync } from 'fs';
import puppeteer from 'file:///C:/Users/Administrator/.workbuddy/binaries/node/workspace/node_modules/puppeteer-core/lib/puppeteer/puppeteer-core.js';

const edge = ['C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
              'C:/Program Files/Microsoft/Edge/Application/msedge.exe'].find(existsSync);
const browser = await puppeteer.launch({
  headless: 'new',
  executablePath: edge,
  args: ['--headless=new', '--disable-gpu', '--no-first-run', '--no-sandbox']
});
try {
  const page = await browser.newPage();
  const url = 'file:///H:/WorkBuddy/工具开发/ima资源下载/build/pdftest/test_lazy.html';
  await page.goto(url, { waitUntil: 'load', timeout: 30000 });

  // 与 C# WaitForImagesScript 等价：滚动全页 + 轮询 img.complete
  await page.evaluate(async () => {
    const sleep = ms => new Promise(r => setTimeout(r, ms));
    const H = () => Math.max(document.body.scrollHeight, document.documentElement.scrollHeight);
    for (let y = 0; y < H() + 700; y += 700) { window.scrollTo(0, y); await sleep(150); }
    window.scrollTo(0, 0); await sleep(300);
    const t0 = Date.now();
    while (Date.now() - t0 < 15000) {
      const imgs = Array.from(document.images || []);
      const pending = imgs.filter(i => !i.complete && i.src);
      if (imgs.length === 0 || pending.length === 0) break;
      await sleep(400);
    }
    await sleep(400);
  });

  await page.pdf({ path: 'H:/WorkBuddy/工具开发/ima资源下载/build/pdftest/out_cdp.pdf', printBackground: true });

  // 对照组：不滚动不等待直接打印（模拟旧行为）
  const page2 = await browser.newPage();
  await page2.goto(url, { waitUntil: 'load', timeout: 30000 });
  await new Promise(r => setTimeout(r, 1200));
  await page2.pdf({ path: 'H:/WorkBuddy/工具开发/ima资源下载/build/pdftest/out_old.pdf', printBackground: true });

  console.log('两份 PDF 已生成');
} finally { await browser.close(); }
