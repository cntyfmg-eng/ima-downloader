// 对照实验：用我们的打包器重打包 ima 内置扩展内容，验证打包器产物能否被导入
import { readFileSync, writeFileSync, mkdirSync, rmSync, cpSync } from 'fs';
import { execFileSync } from 'child_process';
import { join } from 'path';
import AdmZip from 'file:///C:/Users/Administrator/.workbuddy/binaries/node/workspace/node_modules/adm-zip/adm-zip.js';

const NODE = 'C:/Users/Administrator/.workbuddy/binaries/node/versions/22.22.2-3/node.exe';
const BUILTIN = 'C:/Users/Administrator/AppData/Local/ima.copilot/Application/150.0.7871.5349/Extensions/akncmfemomfkkgiffphoedpiioanbflo.crx';
const WORK = 'H:/WorkBuddy/工具开发/ima资源下载/ima-extension/_exp';

// 1. 解包内置 crx
rmSync(WORK, { recursive: true, force: true });
mkdirSync(join(WORK, 'ext'), { recursive: true });
const d = readFileSync(BUILTIN);
const hl = d.readUInt32LE(8);
const zip = new AdmZip(d.subarray(12 + hl));
zip.extractAllTo(join(WORK, 'ext'), true);

// 2. 删除 manifest.key（否则 header 公钥与 manifest key 不匹配被拒）
const mfPath = join(WORK, 'ext', 'manifest.json');
const mf = JSON.parse(readFileSync(mfPath, 'utf8'));
delete mf.key;
writeFileSync(mfPath, JSON.stringify(mf, null, 2));

// 3. 用我们的打包器打包
execFileSync(NODE, ['tools/pack_crx.mjs', join(WORK, 'ext'), join(WORK, 'exp.crx'), 'ima-downloader-key.pem'], { cwd: 'H:/WorkBuddy/工具开发/ima资源下载/ima-extension', stdio: 'inherit' });

// 4. 写入 ima 两个候选 Extensions 目录
const appDir = 'C:/Users/Administrator/AppData/Local/ima.copilot/Application';
const id = readFileSync(join(WORK, 'exp.crx'));
// 读 ID：打包器打印过，这里重新算——从打包输出不可得，改为读 pem 公钥
console.log('实验 crx 已生成:', join(WORK, 'exp.crx'));
