// CRX3 打包器：ima知识库下载器扩展
// 用法: node pack_crx.mjs <extension_dir> <out.crx> [key.pem]
// 首次运行生成 key.pem（保存好，ID 固定靠它）
import { generateKeyPairSync, createHash, createSign } from 'crypto';
import { readFileSync, writeFileSync, existsSync, readdirSync, statSync } from 'fs';
import { join } from 'path';
import AdmZip from 'file:///C:/Users/Administrator/.workbuddy/binaries/node/workspace/node_modules/adm-zip/adm-zip.js';

const [dir, out, keyPath] = process.argv.slice(2);
if (!dir || !out) { console.error('usage: node pack_crx.mjs <dir> <out.crx> [key.pem]'); process.exit(1); }

// ---------- protobuf 手写编码 ----------
const varint = n => { const b = []; do { let x = n & 0x7f; n >>>= 7; if (n) x |= 0x80; b.push(x); } while (n); return Buffer.from(b); };
const tag = (f, w) => varint((f << 3) | w);
const pbBytes = (f, buf) => Buffer.concat([tag(f, 2), varint(buf.length), buf]);
const pbMsg = (f, buf) => pbBytes(f, buf);   // length-delimited

// ---------- 密钥 ----------
let pair;
if (keyPath && existsSync(keyPath)) {
  const pem = readFileSync(keyPath, 'utf8');
  pair = { privateKey: cryptoCreatePrivateKey(pem) };
  console.log('使用已有密钥:', keyPath);
} else {
  const kp = generateKeyPairSync('rsa', { modulusLength: 2048 });
  pair = kp;
  if (keyPath) writeFileSync(keyPath, kp.privateKey.export({ type: 'pkcs8', format: 'pem' }));
  console.log('已生成新密钥 ->', keyPath || '(未保存, ID将不固定)');
}
function cryptoCreatePrivateKey(pem) {
  // 延迟 import 避免顶层复杂
  return createPrivateKeySync(pem);
}
import { createPrivateKey } from 'crypto';
function createPrivateKeySync(pem) { return createPrivateKey(pem); }

const spkiDer = pair.publicKey ? pair.publicKey.export({ type: 'spki', format: 'der' })
  : createPublicKeyFromPrivate(pair.privateKey);
import { createPublicKey } from 'crypto';
function createPublicKeyFromPrivate(priv) { return createPublicKey(priv).export({ type: 'spki', format: 'der' }); }

// ---------- crx_id ----------
const crxId = createHash('sha256').update(spkiDer).digest().subarray(0, 16);
const A = 'abcdefghijklmnop';
const idStr = [...crxId].flatMap(b => [A[b >> 4], A[b & 15]]).join('');
console.log('扩展 ID:', idStr);

// ---------- signed_header_data ----------
const signedHeaderData = pbMsg(1, crxId);

// ---------- header（无签名部分） ----------
const headerNoSig = pbBytes(10000, signedHeaderData);

// ---------- zip ----------
const zip = new AdmZip();
const addDir = (p, base) => {
  for (const name of readdirSync(p)) {
    const full = join(p, name);
    if (statSync(full).isDirectory()) addDir(full, base);
    else zip.addLocalFile(full, p === base ? '' : p.slice(base.length + 1).replace(/\\/g, '/'));
  }
};
addDir(dir, dir);
const zipBuf = zip.toBuffer();

// ---------- 签名（Chromium crx_verifier.cc：覆盖 context + u32le(shd.size) + shd + archive） ----------
const signedData = Buffer.concat([
  Buffer.from('CRX3 SignedData\0'),
  (() => { const b = Buffer.alloc(4); b.writeUInt32LE(signedHeaderData.length); return b; })(),
  signedHeaderData,
  zipBuf
]);
const signature = createSign('RSA-SHA256').update(signedData).sign(pair.privateKey ?? pair);
// Chromium 官方 proto：AsymmetricKeyProof { public_key = 1, signature = 2 }
const sigMsg = Buffer.concat([
  pbBytes(1, spkiDer),
  pbBytes(2, signature)
]);
const header = Buffer.concat([pbMsg(2, sigMsg), headerNoSig]);

// ---------- 组装 ----------
const head = Buffer.alloc(12);
head.write('Cr24', 0); head.writeUInt32LE(3, 4); head.writeUInt32LE(header.length, 8);
writeFileSync(out, Buffer.concat([head, header, zipBuf]));

// 回填 manifest key（SPKI base64）
const mfPath = join(dir, 'manifest.json');
let mf = JSON.parse(readFileSync(mfPath, 'utf8'));
const keyB64 = spkiDer.toString('base64');
if (mf.key !== keyB64) { mf.key = keyB64; writeFileSync(mfPath, JSON.stringify(mf, null, 2)); console.log('manifest.key 已回填'); }

console.log('CRX3 已生成:', out, statSync(out).size, 'bytes');
console.log('EXT_ID=' + idStr);
