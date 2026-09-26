// ima知识库下载器 - service worker：点击工具栏图标打开下载器页面
chrome.action.onClicked.addListener(async (tab) => {
  const url = chrome.runtime.getURL('downloader.html');
  // 已开着就聚焦，不重复开
  const tabs = await chrome.tabs.query({ url: url + '*' });
  if (tabs.length > 0) {
    chrome.tabs.update(tabs[0].id, { active: true });
    chrome.windows.update(tabs[0].windowId, { focused: true });
  } else {
    chrome.tabs.create({ url });
  }
});
