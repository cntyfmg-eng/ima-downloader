# ima 知识库下载器

把 **ima 知识库**（ima.qq.com）里的资料批量下载到本地的两套工具：

1. **Windows 桌面版**（C# WinForms）：填好 Client ID / API Key，选择知识库，一键批量下载。
2. **Edge / Chrome 浏览器扩展**（MV3）：在 ima 网页版注入悬浮球，打开知识库即可一键下载当前资料。

> 下载格式统一：
> - 网页链接 → **PDF**（无头浏览器完整渲染，图片不丢）
> - 文档 / 文件 → **原格式**（docx / pdf / pptx …）
> - 笔记 → **TXT**

---

## 目录结构

```
ima资源下载/
├── src/                      # 桌面版 C# 源码（ImaDownloader）
│   ├── MainForm.cs           # 主窗口
│   ├── Downloader.cs         # 下载 + CDP 无头打印 PDF
│   ├── ImaClient.cs          # ima OpenAPI 封装（鉴权 / 知识库列表 / 媒体信息）
│   ├── SettingsDialog.cs     # 设置（含“清除凭证并保存”）
│   └── resources/            # 二维码 / logo 等资源
├── installer/                # 扩展安装器 C# 源码（ImaExtInstaller）
│   ├── Program.cs            # 释放扩展文件到 %LocalAppData%\DafengImaDownloader\extension
│   ├── MainForm.cs           # 图文安装引导
│   └── resources/
├── ima-extension/
│   ├── extension/            # 扩展源码（MV3）
│   │   ├── manifest.json     # 扩展清单（含 debugger / downloads / storage 等权限）
│   │   ├── content.js        # 注入 ima.qq.com 的绿色悬浮球
│   │   ├── downloader.html    # 下载面板
│   │   ├── downloader.js      # 下载逻辑（网页→PDF / 文档原格式 / 笔记→TXT）
│   │   ├── sw.js             # Service Worker（点击工具栏图标开面板）
│   │   └── icon*.png
│   ├── tools/                # CRX3 打包与调试脚本（Node.js）
│   │   └── pack_crx.mjs       # 手写 CRX3 打包器（RSA-2048 + AsymmetricKeyProof）
│   ├── ima-downloader-key.pem   # 扩展私钥（固定扩展 ID，重建时保持一致）
│   ├── ima_downloader.crx       # 已打包扩展（可直接加载）
│   └── ima_downloader_ext.zip   # 已打包扩展（zip 版）
└── .gitignore
```

---

## 一、桌面版（Windows）

### 构建

需要 **.NET 10 SDK**（或更高版本）。

```bash
# 框架依赖版（本机已装 .NET 运行时时使用，体积小）
dotnet publish src/ImaDownloader.csproj -c Release -r win-x64 --self-contained false \
  -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o publish/desktop

# 完全独立版（双击即可运行，无需安装运行时，约 50MB）
dotnet publish src/ImaDownloader.csproj -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o publish/desktop
```

产物：`publish/desktop/ima知识库下载器.exe`。

### 使用

1. 打开「设置」，填入从 ima 开放平台获取的 **Client ID** 与 **API Key**，选择下载目录，保存。
2. 主界面点击「获取知识库列表」，选择目标知识库。
3. 勾选要下载的资料，点击「开始下载」。
   - 网页链接自动渲染为 PDF（CDP 无头打印，会等待图片加载完成）。
   - 文档 / 文件按原格式保存。
   - 笔记导出为 TXT。
4. 如需清除个人凭证，点「清除凭证并保存」——清除后不再读取任何全局凭证。

---

## 二、浏览器扩展（Edge / Chrome）

### 两种安装方式

**方式 A：用安装器（推荐给普通用户）**

运行 `大冯老师的ima下载插件安装器.exe`，按引导打开 Edge 扩展管理页（`edge://extensions/`）→
开启「开发人员模式」→ 加载已解压的扩展（安装器已把扩展释放到
`%LocalAppData%\DafengImaDownloader\extension`），或直接使用打包好的 `ima_downloader.crx`。

**方式 B：手动加载（开发者）**

1. 浏览器打开 `edge://extensions/` 或 `chrome://extensions/`，开启「开发人员模式」。
2. 「加载已解压的扩展程序」，选择 `ima-extension/extension/` 目录。
3. 打开 https://ima.qq.com 并进入某个知识库，右下角会出现绿色悬浮球，点击即可下载。

### 重新打包扩展

```bash
cd ima-extension
node tools/pack_crx.mjs            # 读取 ima-downloader-key.pem，生成 ima_downloader.crx / .zip
```

> 私钥 `ima-downloader-key.pem` 用于固定扩展 ID `nhadmaebaahcleclmgchnacnlejhkhpi`。
> 若重建时想保持同一 ID，请保留该文件；否则重新生成密钥会得到不同 ID（不影响你本地用已打好的 crx）。

---

## 三、ima OpenAPI 说明（供二次开发）

桌面版与扩展版共用同一套 ima 开放接口：

| 用途 | 接口 |
| --- | --- |
| 可加入的知识库列表 | `openapi/wiki/v1/get_addable_knowledge_base_list` |
| 知识库内的资料列表 | `openapi/wiki/v1/get_knowledge_list` |
| 媒体详情（含 notebook_id） | `openapi/wiki/v1/get_media_info` |
| 笔记正文 | `openapi/note/v1/get_doc_content` |
| 笔记导入 | `openapi/note/v1/import_doc` |

媒体类型（media_type）大致对应：网页(2/6/20)、笔记(11)、文档/文件(原格式)、文件夹(99)。
下载逻辑会跳过文件夹、把网页转 PDF、文档按原格式、笔记导出 TXT。

---

## 四、技术要点

- **PDF 渲染**：早期命令行 `--print-to-pdf --virtual-time-budget` 会“等不及”真实图片（图片转圈/空白）。
  已重写为 **CDP over WebSocket**（桌面版）/ **`chrome.debugger`**（扩展版）：先导航、再全页滚动并轮询
  `img.complete` 与 `data-src` 懒加载，最后 `Page.printToPDF{printBackground:true}` 输出，图片完整。
- **CRX3 打包**：手写 protobuf varint + RSA-2048 / RSA-SHA256，签名覆盖
  `CRX3 SignedData\0` + header 长度 + signed_header_data + zip，已对照 Chromium `crx_verifier` 算法验证。
- **扩展 ID 固定**：由 `ima-downloader-key.pem` 公钥推导，保留密钥即可保持 ID 不变。

---

## 免责声明

本工具仅供**个人知识库备份**使用，请遵守 ima 平台服务条款与相关法律规定，勿用于批量抓取或商业用途。
