// 端到端验证：Edge 加载扩展 → 确认 SW 激活 → 打开 ima.qq.com → 验证悬浮球注入
import { existsSync } from 'fs';
import puppeteer from 'file:///C:/Users/Administrator/.workbuddy/binaries/node/workspace/node_modules/puppeteer-core/lib/puppeteer/puppeteer-core.js';

const extPath = process.env.LOCALAPPDATA + '\\DafengImaDownloader\\extension';
const edge = ['C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
              'C:/Program Files/Microsoft/Edge/Application/msedge.exe'].find(existsSync);
if (!edge) { console.log('未找到 Edge'); process.exit(1); }

const browser = await puppeteer.launch({
  headless: 'new',
  executablePath: edge,
  args: [`--disable-extensions-except=${extPath}`, `--load-extension=${extPath}`, '--no-sandbox']
});
try {
  let sw = null;
  for (let i = 0; i < 20; i++) {
    sw = browser.targets().find(t => t.type() === 'service_worker');
    if (sw) break;
    await new Promise(r => setTimeout(r, 500));
  }
  console.log('扩展 SW:', sw ? 'OK ' + sw.url() : '未加载');

  const page = await browser.newPage();
  await page.goto('https://ima.qq.com/', { waitUntil: 'domcontentloaded', timeout: 45000 }).catch(e => console.log('导航提示:', e.message));
  await new Promise(r => setTimeout(r, 8000));
  console.log('最终 URL:', page.url());
  const injected = await page.evaluate(() => !!document.querySelector('div[title*="ima知识库下载器"]')).catch(e => 'eval失败:' + e.message.slice(0, 80));
  console.log('ima.qq.com 悬浮球注入:', injected === true ? 'OK 成功' : injected);
} finally {
  await browser.close();
}
