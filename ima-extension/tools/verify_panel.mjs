// 验证：点击悬浮球 → iframe 面板是否正常加载下载器页面（而非被 Edge 阻止）
import { existsSync } from 'fs';
import puppeteer from 'file:///C:/Users/Administrator/.workbuddy/binaries/node/workspace/node_modules/puppeteer-core/lib/puppeteer/puppeteer-core.js';

const extPath = process.env.LOCALAPPDATA + '\\DafengImaDownloader\\extension';
const browser = await puppeteer.launch({
  headless: 'new',
  executablePath: ['C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
                   'C:/Program Files/Microsoft/Edge/Application/msedge.exe'].find(existsSync),
  args: ['--disable-extensions-except=' + extPath, '--load-extension=' + extPath, '--no-sandbox']
});
try {
  const page = await browser.newPage();
  await page.goto('https://ima.qq.com/', { waitUntil: 'domcontentloaded', timeout: 45000 });
  await new Promise(r => setTimeout(r, 6000));
  const ball = await page.$('div[title*="ima知识库下载器"]');
  if (!ball) { console.log('悬浮球未注入'); process.exit(1); }
  await ball.click();
  await new Promise(r => setTimeout(r, 6000));
  const res = await page.evaluate(() => {
    const f = [...document.querySelectorAll('iframe')].find(x => x.src && x.src.startsWith('chrome-extension://'));
    if (!f) return '无扩展 iframe';
    try {
      const d = f.contentDocument;
      if (!d) return 'contentDocument 为 null';
      return 'readyState: ' + d.readyState + ' | bodyText: ' + (d.body?.textContent || '').length +
             ' 字符 | title: ' + d.title + ' | h1: ' + (d.querySelector('h1')?.textContent || '无').slice(0, 40) +
             ' | 凭证框: ' + !!d.getElementById('cid');
    } catch (e) { return '访问受限: ' + e.message.slice(0, 60); }
  });
  console.log('面板检查:', res);
} finally { await browser.close(); }
