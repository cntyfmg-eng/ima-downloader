// 用 ima 内置扩展验证 CRX3 签名输入算法
// signed_data = "CRX3 SignedData\0" + crx_id + signed_header_data + header_without_signature
import { readFileSync } from 'fs';
import { createPublicKey, createVerify, createHash } from 'crypto';

const p = 'C:/Users/Administrator/AppData/Local/ima.copilot/Application/150.0.7871.5349/Extensions/akncmfemomfkkgiffphoedpiioanbflo.crx';
const d = readFileSync(p);
const hl = d.readUInt32LE(8);
const header = d.subarray(12, 12 + hl);

// 解析 protobuf，记录每个字段在 header 中的字节范围
function parseRanges(buf) {
  const out = []; let i = 0;
  const varint = i => { let r = 0, s = 0; while (true) { const b = buf[i]; r |= (b & 0x7f) << s; s += 7; if (!(b & 0x80)) return r; i++; } };
  while (i < buf.length) {
    const start = i;
    const key = varint(i); let j = i; i = 0; // 重算 varint 长度
    // 重新按顺序走
    break;
  }
  // 简化：手写解析
  i = 0;
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
    } else { throw new Error('w=' + w); }
  }
  return out;
}

const fields = parseRanges(header);
const sigField = fields.find(x => x.f === 2);
const shdField = fields.find(x => x.f === 10000);
const sigMsg = parseRanges(sigField.v);
const pubkey = sigMsg.find(x => x.f === 1).v;   // SPKI DER (294B)
const signature = sigMsg.find(x => x.f === 2).v; // 256B
const shd = shdField.v;                          // CrxSignedHeader bytes

// header_without_signature：剔除整个 field2 的字节范围
const noSig = Buffer.concat([header.subarray(0, sigField.start), header.subarray(sigField.end)]);

// crx_id = sha256(pubkey)[:16]
const crxId = createHash('sha256').update(pubkey).digest().subarray(0, 16);

// Chromium crx_verifier.cc：签名覆盖 context + u32le(shd.size) + signed_header_data + archive
const zipData = d.subarray(12 + hl);
const sizeBuf = Buffer.alloc(4); sizeBuf.writeUInt32LE(shd.length);
const signedData = Buffer.concat([Buffer.from('CRX3 SignedData\0'), sizeBuf, shd, zipData]);
const ok = createVerify('RSA-SHA256').update(signedData).verify(createPublicKey({ key: pubkey, format: 'der', type: 'spki' }), signature);
console.log('签名输入算法验证:', ok ? '✓ 正确' : '✗ 错误');
console.log('扩展 ID（由公钥算出）:', [...crxId].flatMap(b => ['abcdefghijklmnop'[b >> 4], 'abcdefghijklmnop'[b & 15]]).join(''));
