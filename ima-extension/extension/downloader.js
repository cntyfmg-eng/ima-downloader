const $ = id => document.getElementById(id);
const log = m => { const d = document.createElement('div'); d.textContent = new Date().toTimeString().slice(0,8) + ' ' + m; $('log').appendChild(d); $('log').scrollTop = 1e9; };

let client = null, kbId = '', stack = [], items = [];   // stack: [{id,title}]

// ---------- 凭证 ----------
async function loadCred() {
  const s = await chrome.storage.local.get(['cid','key']);
  $('cid').value = s.cid || ''; $('key').value = s.key || '';
}
$('btnSave').onclick = async () => {
  await chrome.storage.local.set({ cid: $('cid').value.trim(), key: $('key').value.trim() });
  log('凭证已保存到本机（chrome.storage.local）。');
};
$('btnClear').onclick = async () => {
  if (!confirm('确定清除本机保存的 Client ID 与 API Key 吗？')) return;
  await chrome.storage.local.set({ cid: '', key: '' });
  $('cid').value = ''; $('key').value = '';
  log('凭证已清除。');
};

// ---------- OpenAPI ----------
const TYPE_NAME = {99:'文件夹',1:'PDF',2:'网页',3:'Word',4:'PPT',5:'Excel',6:'公众号',7:'Markdown',9:'图片',11:'笔记',12:'AI会话',13:'TXT',14:'Xmind',15:'录音',16:'视频',20:'HTML'};
const DEF_EXT   = {1:'.pdf',3:'.docx',4:'.pptx',5:'.xlsx',7:'.md',9:'.jpg',11:'.txt',13:'.txt',14:'.xmind',15:'.m4a',16:'.mp4',20:'.html'};

async function api(path, body) {
  const cid = $('cid').value.trim(), key = $('key').value.trim();
  if (!cid || !key) throw new Error('请先填写并保存 Client ID 与 API Key');
  const r = await fetch('https://ima.qq.com/' + path, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json',
               'ima-openapi-clientid': cid, 'ima-openapi-apikey': key, 'ima-openapi-ctx': 'skill_version=1.1.10' },
    body: JSON.stringify(body)
  });
  const doc = await r.json();
  if (doc.code !== 0) throw new Error('ima 接口错误(code=' + doc.code + ')：' + (doc.msg || '未知错误'));
  return doc.data || {};
}
const cleanTitle = t => {
  if (!t) return '(未命名)';
  const d = document.createElement('div'); d.innerHTML = t;
  let s = (d.textContent || '').replace(/\s+/g, ' ').trim();
  return s || '(未命名)';
};
const safeName = t => t.replace(/[\\/:*?"<>|\r\n]+/g, '_').slice(0, 80);

async function listAll(path, body, arrKey) {
  const out = []; let cursor = '';
  while (true) {
    const data = await api(path, { ...body, cursor, limit: 50 });
    (data[arrKey] || []).forEach(x => out.push(x));
    if (data.is_end === true || !data.next_cursor) break;
    cursor = data.next_cursor;
  }
  return out;
}

// ---------- 知识库与条目 ----------
$('btnLoad').onclick = async () => {
  try {
    $('btnLoad').disabled = true; log('正在获取知识库列表...');
    const kbs = await listAll('openapi/wiki/v1/get_addable_knowledge_base_list', {}, 'addable_knowledge_base_list');
    const sel = $('kb'); sel.innerHTML = '';
    kbs.forEach(k => { const o = document.createElement('option'); o.value = k.id; o.textContent = k.name; sel.appendChild(o); });
    log(kbs.length ? ('已连接，共 ' + kbs.length + ' 个知识库。') : '该账号下没有可访问的知识库。');
    if (kbs.length) { sel.value = kbs[0].id; openFolder(''); }
  } catch (e) { log('失败：' + e.message); }
  $('btnLoad').disabled = false;
};
$('kb').onchange = () => openFolder('');

function render() {
  const crumb = $('crumb'); crumb.innerHTML = '';
  const root = document.createElement('a'); root.textContent = kbName(); root.onclick = () => openFolder(''); crumb.appendChild(root);
  stack.forEach(s => {
    crumb.appendChild(document.createTextNode(' / '));
    const a = document.createElement('a'); a.textContent = s.title; a.onclick = () => openFolder(s.id); crumb.appendChild(a);
  });
  const list = $('list'); list.innerHTML = '';
  if (!items.length) { list.innerHTML = '<div class="item"><span class="t" style="color:#999">（空）</span></div>'; return; }
  items.forEach((it, i) => {
    const div = document.createElement('div');
    div.className = 'item' + (it.folder ? ' folder' : '');
    const cb = document.createElement('input'); cb.type = 'checkbox'; cb.checked = !!it.checked;
    if (it.folder) cb.disabled = true;
    cb.onchange = () => it.checked = cb.checked;
    const t = document.createElement('span'); t.className = 't'; t.textContent = it.title;
    const tag = document.createElement('span'); tag.className = 'tag' + (it.folder ? ' folder' : (it.skip ? ' skip' : '')); tag.textContent = TYPE_NAME[it.type] || ('类型' + it.type);
    div.append(cb, t, tag);
    if (it.folder) div.ondblclick = () => openFolder(it.id);
    else div.onclick = e => { if (e.target !== cb) { cb.checked = !cb.checked; it.checked = cb.checked; } };
    list.appendChild(div);
  });
}
const kbName = () => $('kb').selectedOptions[0] ? $('kb').selectedOptions[0].textContent : '';

async function openFolder(folderId) {
  kbId = $('kb').value;
  if (!kbId) return;
  if (folderId) {
    const hit = items.find(x => x.id === folderId);
    stack.push({ id: folderId, title: hit ? hit.title : '文件夹' });
  } else stack = [];
  try {
    $('crumb').textContent = '加载中...';
    const fid = folderId || '';
    const body = fid ? { knowledge_base_id: kbId, folder_id: fid } : { knowledge_base_id: kbId };
    const arr = await listAll('openapi/wiki/v1/get_knowledge_list', body, 'knowledge_list');
    items = arr.map(x => {
      const type = x.media_type | 0;
      const folder = type === 99 || String(x.media_id).startsWith('folder_');
      return { id: x.media_id, title: cleanTitle(x.title), type, folder, checked: false, skip: type === 12 };
    });
    render();
    log('已加载 ' + items.length + ' 项。');
  } catch (e) { items = []; render(); log('加载条目失败：' + e.message); }
}

// ---------- 下载 ----------
async function getExtAndUrl(it) {
  const d = await api('openapi/wiki/v1/get_media_info', { media_id: it.id });
  const type = d.media_type | 0;
  let url = '', headers = {};
  if (d.url_info && d.url_info.url) {
    url = d.url_info.url;
    if (d.url_info.headers) for (const k in d.url_info.headers) headers[k] = d.url_info.headers[k];
  }
  let ext = '';
  try { const m = url.split('?')[0].match(/\.([A-Za-z0-9]{2,6})$/); if (m) ext = '.' + m[1].toLowerCase(); } catch {}
  return { type, url, headers, ext: ext || DEF_EXT[type] || '' };
}

async function downloadOne(it) {
  if (it.type === 12) { log('跳过 AI会话（暂无导出接口）：' + it.title); return; }
  if (it.type === 11) {   // 笔记 -> TXT（需先经 get_media_info 换取 notebook_id）
    const info = await api('openapi/wiki/v1/get_media_info', { media_id: it.id });
    const notebookId = info.notebook_ext_info?.notebook_id || '';
    if (!notebookId) throw new Error('未获取到笔记 ID（可能不是本人创建的笔记）');
    const d = await api('openapi/note/v1/get_doc_content', { note_id: notebookId, target_content_format: 0 });
    await saveBlob(new Blob([d.content || ''], { type: 'text/plain;charset=utf-8' }), safeName(it.title) + '.txt');
    return;
  }
  if (it.type === 2 || it.type === 6 || it.type === 20) {   // 网页/公众号/HTML -> 打印为 PDF（失败降级存 HTML）
    const { url } = await getExtAndUrl(it);
    if (!url) throw new Error('未获取到链接');
    try {
      await savePageAsPdf(url, safeName(it.title) + '.pdf');
      return;
    } catch (e) {
      log('PDF 打印失败（' + e.message + '），降级保存 HTML：' + it.title);
      const r = await fetch(url);
      await saveBlob(await r.blob(), safeName(it.title) + '.html');
      return;
    }
  }
  if (it.folder) return;
  // 文档/图片/视频/录音等：原格式直下（优先 downloads API 带凭证头，避免大文件进内存）
  const { url, headers, ext } = await getExtAndUrl(it);
  if (!url) throw new Error('未获取到下载地址');
  const name = safeName(it.title) + ext;
  try {
    const hs = Object.entries(headers).map(([k, v]) => ({ name: k, value: String(v) }));
    await chrome.downloads.download({ url, filename: name, saveAs: false, conflictAction: 'uniquify', headers: hs });
  } catch (e) {
    const r = await fetch(url, { headers });
    if (!r.ok) throw new Error('HTTP ' + r.status);
    await saveBlob(await r.blob(), name);
  }
}

// ---------- 网页转 PDF（chrome.debugger CDP：滚动触发懒加载 + 等图片完成再打印） ----------
const PAGE_READY_SCRIPT = `(async () => {
  const sleep = ms => new Promise(r => setTimeout(r, ms));
  try {
    const H = () => Math.max(document.body ? document.body.scrollHeight : 0,
                             document.documentElement ? document.documentElement.scrollHeight : 0);
    for (let y = 0; y < H() + 700; y += 700) { window.scrollTo(0, y); await sleep(150); }
    window.scrollTo(0, 0); await sleep(300);
    const t0 = Date.now();
    while (Date.now() - t0 < 30000) {
      const imgs = Array.from(document.images || []);
      const pending = imgs.filter(i =>
        (!i.complete && i.src && !i.src.startsWith('data:')) ||
        (i.dataset && i.dataset.src && i.src !== i.dataset.src));
      if (imgs.length === 0 || pending.length === 0) break;
      await sleep(400);
    }
    await sleep(400);
    return true;
  } catch (e) { return false; }
})()`;

async function savePageAsPdf(pageUrl, filename) {
  const tab = await chrome.tabs.create({ url: pageUrl, active: false });
  const debuggee = { tabId: tab.id };
  try {
    await chrome.debugger.attach(debuggee, '1.3');
    // 等标签页加载完成（最多 45s）
    let loaded = false;
    for (let i = 0; i < 90; i++) {
      try {
        const t = await chrome.tabs.get(tab.id);
        if (t.status === 'complete') { loaded = true; break; }
      } catch (e) { throw new Error('页面标签页已关闭'); }
      await new Promise(r => setTimeout(r, 500));
    }
    if (!loaded) throw new Error('页面加载超时');
    // 确认文档就绪（导航期间 evaluate 可能失败，重试）
    for (let i = 0; i < 20; i++) {
      try {
        const r = await chrome.debugger.sendCommand(debuggee, 'Runtime.evaluate',
          { expression: 'document.readyState', returnByValue: true });
        if (r?.result?.value === 'complete') break;
      } catch {}
      await new Promise(r2 => setTimeout(r2, 500));
    }
    // 滚动触发懒加载 + 等全部图片加载完成
    try {
      await chrome.debugger.sendCommand(debuggee, 'Runtime.evaluate',
        { expression: PAGE_READY_SCRIPT, awaitPromise: true, returnByValue: true });
    } catch {}
    // 打印 PDF
    const pdf = await chrome.debugger.sendCommand(debuggee, 'Page.printToPDF', { printBackground: true });
    const b64 = pdf?.data;
    if (!b64) throw new Error('PDF 输出为空');
    const bytes = Uint8Array.from(atob(b64), c => c.charCodeAt(0));
    await saveBlob(new Blob([bytes], { type: 'application/pdf' }), filename);
  } finally {
    try { await chrome.debugger.detach(debuggee); } catch {}
    try { chrome.tabs.remove(tab.id); } catch {}
  }
}

function saveBlob(blob, filename) {
  return new Promise((resolve, reject) => {
    const u = URL.createObjectURL(blob);
    chrome.downloads.download({ url: u, filename, saveAs: false, conflictAction: 'uniquify' }, id => {
      if (id === undefined) { URL.revokeObjectURL(u); return reject(new Error(chrome.runtime.lastError?.message || '下载失败')); }
      setTimeout(() => URL.revokeObjectURL(u), 120000);
      resolve();
    });
  });
}

$('btnAll').onclick = () => { items.forEach(i => { if (!i.folder) i.checked = true; }); render(); };
$('btnNone').onclick = () => { items.forEach(i => i.checked = false); render(); };
$('btnDown').onclick = async () => {
  const targets = items.filter(i => i.checked && !i.folder);
  if (!targets.length) return log('请先勾选要下载的条目（文件夹请双击进入后勾选）。');
  $('btnDown').disabled = true;
  let ok = 0, fail = 0;
  for (let i = 0; i < targets.length; i++) {
    const it = targets[i];
    $('prog').textContent = (i + 1) + '/' + targets.length;
    try { await downloadOne(it); ok++; log('已下载：' + it.title); }
    catch (e) { fail++; log('失败：' + it.title + ' — ' + e.message); }
    await new Promise(r => setTimeout(r, 300));
  }
  $('prog').textContent = '';
  $('btnDown').disabled = false;
  log('完成：成功 ' + ok + '，失败 ' + fail + '。文件已存到浏览器默认下载目录。');
};

loadCred().then(() => log('就绪。填写凭证后点「连接并列出」。'));