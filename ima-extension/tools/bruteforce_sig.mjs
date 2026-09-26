// 暴力枚举 CRX3 签名输入构造，找到正确公式
import { readFileSync } from 'fs';
import { createPublicKey, createVerify, createHash } from 'crypto';

const p = 'C:/Users/Administrator/AppData/Local/ima.copilot/Application/150.0.7871.5349/Extensions/akncmfemomfkkgiffphoedpiioanbflo.crx';
const d = readFileSync(p);
const hl = d.readUInt32LE(8);
const header = d.subarray(12, 12 + hl);
const zipData = d.subarray(12 + hl);

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
      const v = buf.subarray(i, i + ln); i += ln;
      out.push({ f, start: fstart, end: i, v });
    } else throw new Error('w=' + w);
  }
  return out;
}

const fields = parseRanges(header);
const sigField = fields.find(x => x.f === 2);
const shdField = fields.find(x => x.f === 10000);
const sigMsg = parseRanges(sigField.v);
const pubkey = sigMsg.find(x => x.f === 1).v;
const signature = sigMsg.find(x => x.f === 2).v;
const shd = shdField.v;
const shdInner = parseRanges(shd)[0].v;   // crx_id 原始字节
const pubKeyObj = createPublicKey({ key: pubkey, format: 'der', type: 'spki' });
const crxIdHash = createHash('sha256').update(pubkey).digest().subarray(0, 16);

console.log('crx_id(sha256)[:16] == shd.crx_id ?', Buffer.compare(crxIdHash, shdInner) === 0);

const noSig = Buffer.concat([header.subarray(0, sigField.start), header.subarray(sigField.end)]);
const noSigKeepKey = Buffer.concat([
  header.subarray(0, sigField.start + 1),   // 保留 tag 字节？(tag=0x12, 1字节 varint)
  header.subarray(sigField.end)
]);

const CTX1 = Buffer.from('CRX3 SignedData\0');
const CTX2 = Buffer.from('CRX3 SignedData');

const variants = [];
for (const [ctxName, ctx] of [['ctx+00', CTX1], ['ctx', CTX2]])
  for (const [idName, id] of [['+crxId', shdInner], ['-crxId', Buffer.alloc(0)], ['+crxIdHash', crxIdHash]])
    for (const [shdName, s2] of [['+shd', shd], ['-shd', Buffer.alloc(0)]])
      for (const [hName, h] of [['+noSig', noSig], ['+noSigKeepKey', noSigKeepKey], ['-hdr', Buffer.alloc(0)]])
        variants.push([`${ctxName}${idName}${shdName}${hName}`, Buffer.concat([ctx, id, s2, h])]);

for (const [name, data] of variants) {
  if (createVerify('RSA-SHA256').update(data).verify(pubKeyObj, signature)) {
    console.log('✓ 命中公式:', name);
    process.exit(0);
  }
}
console.log('✗ 所有变体均失败');
