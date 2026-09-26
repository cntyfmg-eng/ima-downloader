// 自校验：解析我们自己的 crx，用 Chromium 公式验签
import { readFileSync } from 'fs';
import { createPublicKey, createVerify, createHash } from 'crypto';

const p = process.argv[2];
const d = readFileSync(p);
const hl = d.readUInt32LE(8);
const header = d.subarray(12, 12 + hl);
const zipData = d.subarray(12 + hl);
console.log('header_len:', hl, 'zip:', zipData.length);

function parseRanges(buf) {
  const out = []; let i = 0;
  while (i < buf.length) {
    const fstart = i;
    let key = 0, s = 0;
    while (true) { const b = buf[i++]; key |= (b & 0x7f) << s; s += 7; if (!(b & 0x80)) break; }
    const f = key >> 3, w = key & 7;
    if (w === 2) {
      let ln = 0; s = 0;
      while (true) { const b = buf[i++]; ln |= (b & 0x7f) << s; s += 7; if (!(b & 0x80)) break; }
      out.push({ f, start: fstart, end: i, v: buf.subarray(i, i + ln) }); i += ln;
    } else throw new Error('unexpected wiretype ' + w);
  }
  return out;
}

const fields = parseRanges(header);
console.log('header 字段:', fields.map(x => x.f).join(','));
const sigField = fields.find(x => x.f === 2);
const shdField = fields.find(x => x.f === 10000);
const sigMsg = parseRanges(sigField.v);
console.log('sig msg 字段:', sigMsg.map(x => x.f).join(','));
const pubkey = sigMsg.find(x => x.f === 1).v;
const signature = sigMsg.find(x => x.f === 2).v;
const shd = shdField.v;
const shdInner = parseRanges(shd);
console.log('shd 内字段:', shdInner.map(x => x.f).join(','), 'crx_id bytes:', shdInner[0].v.length);

// ID 校验
const crxId = createHash('sha256').update(pubkey).digest().subarray(0, 16);
const A = 'abcdefghijklmnop';
const idStr = [...crxId].flatMap(b => [A[b >> 4], A[b & 15]]).join('');
console.log('ID(公钥算):', idStr, '| shd内ID匹配:', Buffer.compare(crxId, shdInner[0].v) === 0);

// 签名验证
const sizeBuf = Buffer.alloc(4); sizeBuf.writeUInt32LE(shd.length);
const signedData = Buffer.concat([Buffer.from('CRX3 SignedData\0'), sizeBuf, shd, zipData]);
const ok = createVerify('RSA-SHA256').update(signedData).verify(createPublicKey({ key: pubkey, format: 'der', type: 'spki' }), signature);
console.log('验签:', ok ? '✓ 通过' : '✗ 失败');

// zip 完整性
import AdmZip from 'file:///C:/Users/Administrator/.workbuddy/binaries/node/workspace/node_modules/adm-zip/adm-zip.js';
try {
  const z = new AdmZip(zipData);
  const mf = JSON.parse(z.readAsText('manifest.json'));
  console.log('zip OK, manifest:', mf.name, mf.version, '| key存在:', !!mf.key, '| key匹配header公钥:', mf.key === pubkey.toString('base64'));
} catch (e) { console.log('zip 解析失败:', e.message); }
