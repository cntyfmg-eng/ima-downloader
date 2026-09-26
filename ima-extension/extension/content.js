// ima.qq.com 页面注入：右下角悬浮球 → 点击展开下载器面板（iframe 加载扩展页面）
(function () {
  if (window.__dafengImaExt) return;
  window.__dafengImaExt = true;

  const ball = document.createElement('div');
  ball.innerHTML = '⬇';
  Object.assign(ball.style, {
    position: 'fixed', right: '24px', bottom: '88px', zIndex: 2147483000,
    width: '52px', height: '52px', borderRadius: '50%',
    background: 'linear-gradient(135deg,#00785A,#00a37c)', color: '#fff',
    fontSize: '24px', lineHeight: '52px', textAlign: 'center',
    cursor: 'pointer', boxShadow: '0 4px 16px rgba(0,120,90,.45)',
    userSelect: 'none', border: '2px solid #fff'
  });
  ball.title = 'ima知识库下载器（大冯老师）';
  document.documentElement.appendChild(ball);

  let panel = null;
  ball.addEventListener('click', () => {
    if (panel) { panel.remove(); panel = null; return; }
    panel = document.createElement('div');
    Object.assign(panel.style, {
      position: 'fixed', right: '24px', bottom: '150px', zIndex: 2147483000,
      width: '560px', height: '640px', maxHeight: '80vh',
      borderRadius: '14px', overflow: 'hidden',
      boxShadow: '0 8px 32px rgba(0,0,0,.28)', background: '#f4f7f6',
      border: '1px solid #d8e2de'
    });
    const iframe = document.createElement('iframe');
    iframe.src = chrome.runtime.getURL('downloader.html');
    iframe.style.cssText = 'width:100%;height:100%;border:0;';
    panel.appendChild(iframe);
    document.documentElement.appendChild(panel);
  });
})();
