// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
// Bitcoin, Litecoin and Dogecoin: addresses, balance, history, and sending from the Sillet window.
// Same recovery phrase and same first address as the Sillet app. Websites cannot use these coins, only EVM networks
import { HDNodeWallet, Mnemonic, SigningKey, sha256, ripemd160, getBytes, hexlify } from 'ethers';

export const NETS = {
  bitcoin: {
    id: 'bitcoin', name: 'Bitcoin', symbol: 'BTC', decimals: 8, type: 'p2wpkh', hrp: 'bc', p2pkh: 0x00, p2sh: [0x05],
    path: (i) => `m/84'/0'/${i}'/0/0`, dust: 294, minRate: 1, txVersion: 2, sequence: 0xfffffffd,
    explorer: 'https://mempool.space', api: 'esplora', bases: ['https://mempool.space/api', 'https://blockstream.info/api'], cg: 'bitcoin',
  },
  litecoin: {
    id: 'litecoin', name: 'Litecoin', symbol: 'LTC', decimals: 8, type: 'p2wpkh', hrp: 'ltc', p2pkh: 0x30, p2sh: [0x32, 0x05],
    path: (i) => `m/84'/2'/${i}'/0/0`, dust: 294, minRate: 1, txVersion: 2, sequence: 0xfffffffd,
    explorer: 'https://litecoinspace.org', api: 'esplora', bases: ['https://litecoinspace.org/api'], cg: 'litecoin',
  },
  bitcoincash: {
    id: 'bitcoincash', name: 'Bitcoin Cash', symbol: 'BCH', decimals: 8, type: 'p2pkh', hrp: null, p2pkh: 0x00, p2sh: [0x05], cash: true, forkId: true,
    path: (i) => `m/44'/145'/${i}'/0/0`, dust: 546, minRate: 1, txVersion: 1, sequence: 0xffffffff,
    explorer: 'https://blockchair.com/bitcoin-cash', explorerTx: '/transaction/', api: 'blockbook', bases: ['https://bch1.trezor.io/api/v2', 'https://bch2.trezor.io/api/v2'], cg: 'bitcoin-cash',
  },
  dogecoin: {
    id: 'dogecoin', name: 'Dogecoin', symbol: 'DOGE', decimals: 8, type: 'p2pkh', hrp: null, p2pkh: 0x1e, p2sh: [0x16],
    path: (i) => `m/44'/3'/${i}'/0/0`, dust: 1000000, minRate: 1000, txVersion: 1, sequence: 0xffffffff,
    explorer: 'https://dogechain.info', api: 'blockcypher', bases: ['https://api.blockcypher.com/v1/doge/main'], cg: 'dogecoin',
  },
};
// Only the automated browser test sets this, to point the explorers at a fake local server
// eslint-disable-next-line no-undef
const TEST = typeof __SILLET_TEST_BASE__ === 'string' ? __SILLET_TEST_BASE__ : '';
if (TEST) { NETS.bitcoin.bases = [TEST + '/esplora']; NETS.litecoin.bases = [TEST + '/esplora']; NETS.dogecoin.bases = [TEST + '/cypher']; NETS.bitcoincash.bases = [TEST + '/blockbook']; }
export const NET_IDS = Object.keys(NETS);
export const isNet = (id) => typeof id === 'string' && Object.prototype.hasOwnProperty.call(NETS, id);
export const publicNet = (n) => ({ chainId: n.id, utxo: true, name: n.name, symbol: n.symbol, decimals: n.decimals, explorer: n.explorer, type: n.type, txPath: n.explorerTx || '/tx/', tokens: [] });

// ---------- bytes and hashes ----------

const u8 = (x) => (x instanceof Uint8Array ? x : Array.isArray(x) ? Uint8Array.from(x) : getBytes(x));
const cat = (...parts) => {
  const arr = parts.map(u8);
  const out = new Uint8Array(arr.reduce((n, p) => n + p.length, 0));
  let o = 0;
  for (const p of arr) { out.set(p, o); o += p.length; }
  return out;
};
const sha256d = (b) => getBytes(sha256(getBytes(sha256(b))));
const hash160 = (b) => getBytes(ripemd160(getBytes(sha256(b))));
const le = (n, bytes) => { const o = new Uint8Array(bytes); let v = BigInt(n); for (let i = 0; i < bytes; i++) { o[i] = Number(v & 0xffn); v >>= 8n; } return o; };
const varint = (n) => (n < 0xfd ? Uint8Array.of(n) : cat(Uint8Array.of(0xfd), le(n, 2)));
const push = (b) => { const x = u8(b); if (x.length >= 0x4c) throw new Error('Script push too big'); return cat(Uint8Array.of(x.length), x); };
const rev = (b) => Uint8Array.from(u8(b)).reverse();
export const toHex = (b) => hexlify(b).slice(2);
const fromHex = (s) => getBytes('0x' + s);

// ---------- base58check and bech32 ----------

const B58 = '123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz';
function b58encode(bytes) {
  let n = 0n;
  for (const b of bytes) n = (n << 8n) | BigInt(b);
  let s = '';
  while (n > 0n) { s = B58[Number(n % 58n)] + s; n /= 58n; }
  for (const b of bytes) { if (b === 0) s = '1' + s; else break; }
  return s;
}
function b58decode(str) {
  let n = 0n;
  for (const ch of str) {
    const i = B58.indexOf(ch);
    if (i < 0) throw new Error('bad base58');
    n = n * 58n + BigInt(i);
  }
  const out = [];
  while (n > 0n) { out.unshift(Number(n & 0xffn)); n >>= 8n; }
  let zeros = 0;
  for (const ch of str) { if (ch === '1') zeros++; else break; }
  return Uint8Array.from([...new Array(zeros).fill(0), ...out]);
}
const b58check = (version, hash) => { const p = cat(Uint8Array.of(version), hash); return b58encode(cat(p, sha256d(p).slice(0, 4))); };
function b58checkDecode(str) {
  const raw = b58decode(str);
  if (raw.length < 5) throw new Error('short');
  const body = raw.slice(0, -4);
  const sum = sha256d(body).slice(0, 4);
  for (let i = 0; i < 4; i++) if (sum[i] !== raw[raw.length - 4 + i]) throw new Error('checksum');
  return { version: body[0], hash: body.slice(1) };
}

const CHARSET = 'qpzry9x8gf2tvdw0s3jn54khce6mua7l';
const GEN = [0x3b6a57b2, 0x26508e6d, 0x1ea119fa, 0x3d4233dd, 0x2a1462b3];
function polymod(values) {
  let chk = 1;
  for (const v of values) {
    const top = chk >>> 25;
    chk = ((chk & 0x1ffffff) << 5) ^ v;
    for (let i = 0; i < 5; i++) if ((top >>> i) & 1) chk ^= GEN[i];
  }
  return chk >>> 0;
}
const hrpExpand = (hrp) => [...[...hrp].map((c) => c.charCodeAt(0) >>> 5), 0, ...[...hrp].map((c) => c.charCodeAt(0) & 31)];
function convertBits(data, from, to, pad) {
  let acc = 0; let bits = 0;
  const out = [];
  const maxv = (1 << to) - 1;
  for (const v of data) {
    if (v < 0 || v >> from) return null;
    acc = (acc << from) | v;
    bits += from;
    while (bits >= to) { bits -= to; out.push((acc >> bits) & maxv); }
  }
  if (pad) { if (bits) out.push((acc << (to - bits)) & maxv); } else if (bits >= from || ((acc << (to - bits)) & maxv)) return null;
  return out;
}
function bech32Encode(hrp, version, program) {
  const data = [version, ...convertBits(program, 8, 5, true)];
  const mod = polymod([...hrpExpand(hrp), ...data, 0, 0, 0, 0, 0, 0]) ^ (version === 0 ? 1 : 0x2bc830a3);
  const chk = Array.from({ length: 6 }, (_, i) => (mod >>> (5 * (5 - i))) & 31);
  return hrp + '1' + [...data, ...chk].map((d) => CHARSET[d]).join('');
}
function bech32Decode(addr, hrp) {
  if (addr !== addr.toLowerCase() && addr !== addr.toUpperCase()) return null;
  const a = addr.toLowerCase();
  const pos = a.lastIndexOf('1');
  if (pos < 1 || pos + 7 > a.length || a.length > 90 || a.slice(0, pos) !== hrp) return null;
  const data = [];
  for (const ch of a.slice(pos + 1)) { const i = CHARSET.indexOf(ch); if (i < 0) return null; data.push(i); }
  const m = polymod([...hrpExpand(hrp), ...data]);
  const version = data[0];
  if (version > 16 || m !== (version === 0 ? 1 : 0x2bc830a3)) return null;
  const prog = convertBits(data.slice(1, -6), 5, 8, false);
  if (!prog || prog.length < 2 || prog.length > 40) return null;
  if (version === 0 && prog.length !== 20 && prog.length !== 32) return null;
  return { version, program: Uint8Array.from(prog) };
}

// CashAddr, the address format of Bitcoin Cash
const CASH_PREFIX = 'bitcoincash';
const CASH_GEN = [0x98f2bc8e61n, 0x79b76d99e2n, 0xf33e5fb3c4n, 0xae2eabe2a8n, 0x1e4f43e470n];
function cashPolymod(values) {
  let c = 1n;
  for (const v of values) {
    const c0 = c >> 35n;
    c = ((c & 0x07ffffffffn) << 5n) ^ BigInt(v);
    for (let i = 0; i < 5; i++) if ((c0 >> BigInt(i)) & 1n) c ^= CASH_GEN[i];
  }
  return c ^ 1n;
}
const cashPrefixValues = [...CASH_PREFIX].map((ch) => ch.charCodeAt(0) & 31);
function cashEncode(hash, type = 0, withPrefix = true) {
  const data = convertBits(cat([type << 3], hash), 8, 5, true);
  const chk = cashPolymod([...cashPrefixValues, 0, ...data, 0, 0, 0, 0, 0, 0, 0, 0]);
  const check = Array.from({ length: 8 }, (_, i) => Number((chk >> BigInt(5 * (7 - i))) & 31n));
  return (withPrefix ? CASH_PREFIX + ':' : '') + [...data, ...check].map((d) => CHARSET[d]).join('');
}
function cashDecode(text) {
  let a = String(text || '').trim();
  if (a !== a.toLowerCase() && a !== a.toUpperCase()) return null;
  a = a.toLowerCase();
  if (a.startsWith(CASH_PREFIX + ':')) a = a.slice(CASH_PREFIX.length + 1);
  else if (a.includes(':')) return null;
  const data = [];
  for (const ch of a) { const i = CHARSET.indexOf(ch); if (i < 0) return null; data.push(i); }
  if (data.length < 9 || cashPolymod([...cashPrefixValues, 0, ...data]) !== 0n) return null;
  const bytes = convertBits(data.slice(0, -8), 5, 8, false);
  if (!bytes || bytes.length !== 21) return null;
  const type = bytes[0] >> 3;
  if ((bytes[0] & 7) !== 0 || (type !== 0 && type !== 1)) return null;
  return { type, hash: Uint8Array.from(bytes.slice(1)) };
}
const cashBare = (a) => String(a || '').toLowerCase().replace(/^bitcoincash:/, '');

// ---------- scripts and addresses ----------

const p2pkhScript = (h) => cat([0x76, 0xa9, 0x14], h, [0x88, 0xac]);
const p2shScript = (h) => cat([0xa9, 0x14], h, [0x87]);

export function scriptFor(net, address) {
  const a = String(address || '').trim();
  if (net.cash) {
    const c = cashDecode(a);
    if (c) return c.type === 0 ? p2pkhScript(c.hash) : p2shScript(c.hash);
  }
  if (net.hrp) {
    const d = bech32Decode(a, net.hrp);
    if (d) {
      if (d.version === 0) return cat([0x00, d.program.length], d.program);
      return cat([0x50 + d.version, d.program.length], d.program);
    }
  }
  try {
    const { version, hash } = b58checkDecode(a);
    if (hash.length === 20) {
      if (version === net.p2pkh) return p2pkhScript(hash);
      if (net.p2sh.includes(version)) return p2shScript(hash);
    }
  } catch (e) { /* not base58 */ }
  return null;
}
export const validAddress = (netId, address) => !!(isNet(netId) && scriptFor(NETS[netId], address));

function deriveKey(root, net, index) {
  const w = root.derivePath(net.path(index));
  return { priv: w.privateKey, pub: getBytes(w.publicKey) };
}
const rootOf = (mnemonic) => HDNodeWallet.fromSeed(Mnemonic.fromPhrase(mnemonic).computeSeed());

function addressOf(net, pub) {
  const h = hash160(pub);
  if (net.cash) return cashEncode(h);
  return net.type === 'p2wpkh' ? bech32Encode(net.hrp, 0, h) : b58check(net.p2pkh, h);
}

export function addressesFor(mnemonic, index) {
  const root = rootOf(mnemonic);
  return Object.fromEntries(NET_IDS.map((id) => [id, addressOf(NETS[id], deriveKey(root, NETS[id], index).pub)]));
}

// ---------- network ----------

async function getJson(url, init) {
  const ctl = new AbortController();
  const timer = setTimeout(() => ctl.abort(), 12000);
  try {
    const r = await fetch(url, { ...init, signal: ctl.signal, credentials: 'omit', referrerPolicy: 'no-referrer' });
    const text = await r.text();
    if (!r.ok) throw new Error(text && text.length < 200 ? text : 'The network is not answering (' + r.status + ')');
    try { return JSON.parse(text); } catch (e) { return text; }
  } finally { clearTimeout(timer); }
}
async function firstOk(net, fn) {
  let last;
  for (const base of net.bases) {
    try { return await fn(base); } catch (e) { last = e; }
  }
  throw last || new Error('The network is not answering');
}

const bb = (net, path, init) => firstOk(net, (b) => getJson(b + path, init));
const bare = (net, address) => (net.cash ? cashBare(address) : address);

export async function getBalance(netId, address) {
  const net = NETS[netId];
  if (net.api === 'blockbook') {
    const j = await bb(net, `/address/${bare(net, address)}?details=basic`);
    return { balance: (BigInt(j.balance) + BigInt(j.unconfirmedBalance || 0)).toString(), pending: String(j.unconfirmedBalance || 0) };
  }
  if (net.api === 'esplora') {
    const j = await firstOk(net, (b) => getJson(`${b}/address/${address}`));
    const conf = BigInt(j.chain_stats.funded_txo_sum) - BigInt(j.chain_stats.spent_txo_sum);
    const pend = BigInt(j.mempool_stats.funded_txo_sum) - BigInt(j.mempool_stats.spent_txo_sum);
    return { balance: (conf + pend).toString(), pending: pend.toString() };
  }
  const j = await getJson(`${net.bases[0]}/addrs/${address}/balance`);
  return { balance: String(j.final_balance), pending: String(j.unconfirmed_balance || 0) };
}

async function getUtxos(net, address) {
  if (net.api === 'blockbook') {
    const list = await bb(net, `/utxo/${bare(net, address)}`);
    return list.map((u) => ({ txid: u.txid, vout: u.vout, value: BigInt(u.value), confirmed: (u.confirmations || 0) > 0 }));
  }
  if (net.api === 'esplora') {
    const list = await firstOk(net, (b) => getJson(`${b}/address/${address}/utxo`));
    return list.map((u) => ({ txid: u.txid, vout: u.vout, value: BigInt(u.value), confirmed: !!(u.status && u.status.confirmed) }));
  }
  const j = await getJson(`${net.bases[0]}/addrs/${address}?unspentOnly=true&includeScript=false&limit=2000`);
  const refs = [...(j.txrefs || []), ...(j.unconfirmed_txrefs || [])];
  return refs.filter((r) => r.tx_output_n >= 0).map((r) => ({ txid: r.tx_hash, vout: r.tx_output_n, value: BigInt(r.value), confirmed: (r.confirmations || 0) > 0 }));
}

export async function getFeeRates(netId) {
  const net = NETS[netId];
  let r;
  try {
    if (net.api === 'blockbook') {
      r = { low: 1, market: 1.1, fast: 2 };
    } else if (net.api === 'esplora') {
      try {
        const j = await firstOk(net, (b) => getJson(`${b}/v1/fees/recommended`));
        r = { low: j.hourFee, market: j.halfHourFee, fast: j.fastestFee };
      } catch (e) {
        const j = await firstOk(net, (b) => getJson(`${b}/fee-estimates`));
        r = { low: j['6'], market: j['3'], fast: j['1'] };
      }
    } else {
      const j = await getJson(net.bases[0]);
      r = { low: j.low_fee_per_kb / 1000, market: j.medium_fee_per_kb / 1000, fast: j.high_fee_per_kb / 1000 };
    }
  } catch (e) {
    throw new Error('Could not get the network fee. Try again in a moment');
  }
  const out = {};
  for (const k of ['low', 'market', 'fast']) out[k] = Math.max(Number(r[k]) || net.minRate, net.minRate);
  out.market = Math.max(out.market, out.low);
  out.fast = Math.max(out.fast, out.market);
  return out;
}

export async function broadcast(netId, hex) {
  const net = NETS[netId];
  try {
    if (net.api === 'blockbook') {
      const j = await bb(net, '/sendtx/' + hex);
      if (j.error) throw new Error(typeof j.error === 'string' ? j.error : j.error.message);
      return j.result;
    }
    if (net.api === 'esplora') {
      return await firstOk(net, async (b) => {
        const txt = await getJson(`${b}/tx`, { method: 'POST', body: hex, headers: { 'content-type': 'text/plain' } });
        return String(txt).trim();
      });
    }
    const j = await getJson(`${net.bases[0]}/txs/push`, { method: 'POST', body: JSON.stringify({ tx: hex }), headers: { 'content-type': 'application/json' } });
    if (j.error) throw new Error(j.error);
    return j.tx.hash;
  } catch (e) {
    throw new Error('The network did not accept the transaction: ' + String(e.message || e).slice(0, 160));
  }
}

export async function getTxStatus(netId, hash) {
  const net = NETS[netId];
  try {
    if (net.api === 'blockbook') {
      const j = await bb(net, `/tx/${hash}`);
      return (j.confirmations || 0) > 0 ? 'confirmed' : 'pending';
    }
    if (net.api === 'esplora') {
      const j = await firstOk(net, (b) => getJson(`${b}/tx/${hash}/status`));
      return j.confirmed ? 'confirmed' : 'pending';
    }
    const j = await getJson(`${net.bases[0]}/txs/${hash}`);
    return (j.confirmations || 0) > 0 ? 'confirmed' : 'pending';
  } catch (e) { return 'pending'; }
}

export async function getHistory(netId, address) {
  const net = NETS[netId];
  if (net.api === 'blockbook') {
    const me = cashBare(address);
    const j = await bb(net, `/address/${me}?details=txs&pageSize=25`);
    const same = (list) => Array.isArray(list) && list.some((x) => cashBare(x) === me);
    return (j.transactions || []).map((tx) => {
      let recv = 0n; let spent = 0n; let other = null;
      for (const i of tx.vin || []) if (same(i.addresses)) spent += BigInt(i.value || 0);
      for (const o of tx.vout || []) { if (same(o.addresses)) recv += BigInt(o.value || 0); else if (!other && o.addresses && o.addresses[0]) other = o.addresses[0]; }
      const delta = recv - spent;
      const from = ((tx.vin || []).find((i) => i.addresses && i.addresses[0] && cashBare(i.addresses[0]) !== me) || {}).addresses;
      return { hash: tx.txid, delta: delta.toString(), counterparty: delta < 0n ? other : (from && from[0]) || null, time: tx.blockTime ? tx.blockTime * 1000 : null, confirmed: (tx.confirmations || 0) > 0 };
    });
  }
  if (net.api === 'esplora') {
    const list = await firstOk(net, (b) => getJson(`${b}/address/${address}/txs`));
    return list.slice(0, 25).map((tx) => {
      let recv = 0n; let spent = 0n; let other = null;
      for (const i of tx.vin) if (i.prevout && i.prevout.scriptpubkey_address === address) spent += BigInt(i.prevout.value);
      for (const o of tx.vout) { if (o.scriptpubkey_address === address) recv += BigInt(o.value); else if (!other && o.scriptpubkey_address) other = o.scriptpubkey_address; }
      const delta = recv - spent;
      return { hash: tx.txid, delta: delta.toString(), counterparty: delta < 0n ? other : (tx.vin[0] && tx.vin[0].prevout && tx.vin[0].prevout.scriptpubkey_address) || null, time: tx.status && tx.status.block_time ? tx.status.block_time * 1000 : null, confirmed: !!(tx.status && tx.status.confirmed) };
    });
  }
  const j = await getJson(`${net.bases[0]}/addrs/${address}?limit=30&includeScript=false`);
  const sums = new Map();
  for (const r of [...(j.unconfirmed_txrefs || []), ...(j.txrefs || [])]) {
    const cur = sums.get(r.tx_hash) || { delta: 0n, time: r.confirmed ? Date.parse(r.confirmed) : null, confirmed: (r.confirmations || 0) > 0 };
    cur.delta += r.tx_input_n >= 0 ? -BigInt(r.value) : BigInt(r.value);
    sums.set(r.tx_hash, cur);
  }
  return [...sums.entries()].slice(0, 25).map(([hash, v]) => ({ hash, delta: v.delta.toString(), counterparty: null, time: v.time, confirmed: v.confirmed }));
}

// ---------- building and signing ----------

const IN_SIZE = { p2wpkh: 68, p2pkh: 148 };
const OVERHEAD = { p2wpkh: 10.5, p2pkh: 10 };
const outSize = (script) => 8 + 1 + script.length;
const sizeOf = (net, inputs, scripts) => Math.ceil(OVERHEAD[net.type] + inputs * IN_SIZE[net.type] + scripts.reduce((n, s) => n + outSize(s), 0));
const feeFor = (net, inputs, scripts, rate) => BigInt(Math.ceil(sizeOf(net, inputs, scripts) * rate));

// Picks the coins to spend. amount null means "send everything"
export function plan(net, utxos, destScript, changeScript, amount, rate) {
  const sorted = [...utxos].sort((a, b) => (a.confirmed === b.confirmed ? (b.value > a.value ? 1 : b.value < a.value ? -1 : 0) : a.confirmed ? -1 : 1));
  const dust = BigInt(net.dust);
  if (!sorted.length) throw new Error('You have no ' + net.symbol + ' available to send');
  if (amount == null) {
    const total = sorted.reduce((n, u) => n + u.value, 0n);
    const fee = feeFor(net, sorted.length, [destScript], rate);
    const send = total - fee;
    if (send < dust) throw new Error('The balance does not cover the fee');
    return { inputs: sorted, outputs: [{ value: send, script: destScript }], fee, amount: send, change: 0n };
  }
  if (amount < dust) throw new Error('The minimum amount is ' + fmtUnits(dust, net.decimals) + ' ' + net.symbol);
  const picked = [];
  let total = 0n;
  for (const u of sorted) {
    picked.push(u);
    total += u.value;
    const withChange = feeFor(net, picked.length, [destScript, changeScript], rate);
    if (total >= amount + withChange + dust) {
      return { inputs: picked, outputs: [{ value: amount, script: destScript }, { value: total - amount - withChange, script: changeScript }], fee: withChange, amount, change: total - amount - withChange };
    }
    const noChange = feeFor(net, picked.length, [destScript], rate);
    if (total >= amount + noChange) {
      return { inputs: picked, outputs: [{ value: amount, script: destScript }], fee: total - amount, amount, change: 0n };
    }
  }
  const need = amount + feeFor(net, sorted.length, [destScript], rate);
  throw new Error('You do not have enough ' + net.symbol + '. You need ' + fmtUnits(need, net.decimals) + ' including the fee');
}

export const fmtUnits = (v, d) => {
  const neg = v < 0n;
  const x = (neg ? -v : v).toString().padStart(d + 1, '0');
  const whole = x.slice(0, -d); const frac = x.slice(-d).replace(/0+$/, '');
  return (neg ? '-' : '') + whole + (frac ? '.' + frac : '');
};
export function parseAmount(str, d) {
  const s = String(str || '').trim().replace(',', '.');
  if (!/^\d*\.?\d*$/.test(s) || s === '' || s === '.') throw new Error('Enter a valid amount');
  const [w, f = ''] = s.split('.');
  if (f.length > d) throw new Error('Too many decimals for this coin');
  return BigInt(w || '0') * 10n ** BigInt(d) + BigInt((f + '0'.repeat(d)).slice(0, d) || '0');
}

const derSig = (digest, priv, hashType = 0x01) => {
  const sig = new SigningKey(priv).sign(digest);
  const part = (hex) => { let b = [...getBytes(hex)]; while (b.length > 1 && b[0] === 0) b.shift(); if (b[0] & 0x80) b.unshift(0); return Uint8Array.from(b); };
  const r = part(sig.r); const s = part(sig.s);
  return cat([0x30, 2 + r.length + 2 + s.length, 0x02, r.length], r, [0x02, s.length], s, [hashType]);
};

const serOutputs = (outs) => cat(...outs.map((o) => cat(le(o.value, 8), varint(o.script.length), o.script)));

// Returns the signed transaction as hex
export function signTx(net, p, key, ownScript) {
  const { inputs, outputs } = p;
  const version = le(net.txVersion, 4); const seq = le(net.sequence, 4); const lock = le(0, 4);
  const outpoint = (u) => cat(rev(fromHex(u.txid)), le(u.vout, 4));
  if (net.type === 'p2wpkh') {
    const hashPrevouts = sha256d(cat(...inputs.map(outpoint)));
    const hashSequence = sha256d(cat(...inputs.map(() => seq)));
    const hashOutputs = sha256d(serOutputs(outputs));
    const scriptCode = cat([0x19, 0x76, 0xa9, 0x14], hash160(key.pub), [0x88, 0xac]);
    const wit = inputs.map((u) => {
      const pre = cat(version, hashPrevouts, hashSequence, outpoint(u), scriptCode, le(u.value, 8), seq, hashOutputs, lock, le(1, 4));
      const sig = derSig(sha256d(pre), key.priv);
      return cat([0x02], varint(sig.length), sig, varint(key.pub.length), key.pub);
    });
    return toHex(cat(version, [0x00, 0x01], varint(inputs.length), ...inputs.map((u) => cat(outpoint(u), [0x00], seq)), varint(outputs.length), serOutputs(outputs), ...wit, lock));
  }
  if (net.forkId) {
    // Bitcoin Cash: the same digest as segwit, with the fork id flag, and the old style transaction
    const hashPrevouts = sha256d(cat(...inputs.map(outpoint)));
    const hashSequence = sha256d(cat(...inputs.map(() => seq)));
    const hashOutputs = sha256d(serOutputs(outputs));
    const scriptSigs = inputs.map((u) => {
      const pre = cat(version, hashPrevouts, hashSequence, outpoint(u), varint(ownScript.length), ownScript, le(u.value, 8), seq, hashOutputs, lock, le(0x41, 4));
      const sig = derSig(sha256d(pre), key.priv, 0x41);
      return cat(push(sig), push(key.pub));
    });
    return toHex(cat(version, varint(inputs.length), ...inputs.map((u, i) => cat(outpoint(u), varint(scriptSigs[i].length), scriptSigs[i], seq)), varint(outputs.length), serOutputs(outputs), lock));
  }
  // legacy (Dogecoin)
  const body = (signIdx, scripts) => cat(version, varint(inputs.length), ...inputs.map((u, i) => cat(outpoint(u), varint((scripts[i] || new Uint8Array()).length), scripts[i] || new Uint8Array(), seq)), varint(outputs.length), serOutputs(outputs), lock);
  const scriptSigs = inputs.map((u, i) => {
    const scripts = inputs.map((_, j) => (j === i ? ownScript : new Uint8Array()));
    const digest = sha256d(cat(body(i, scripts), le(1, 4)));
    const sig = derSig(digest, key.priv);
    return cat(push(sig), push(key.pub));
  });
  return toHex(body(-1, scriptSigs));
}

// Everything the Sillet window needs to show a payment before it is sent
export async function prepareSend({ netId, address, to, amount }) {
  const net = NETS[netId];
  const dest = scriptFor(net, to);
  if (!dest) throw new Error('That is not a valid ' + net.name + ' address');
  const own = scriptFor(net, address);
  const [utxos, rates] = await Promise.all([getUtxos(net, address), getFeeRates(netId)]);
  const amt = amount == null || amount === 'max' ? null : parseAmount(amount, net.decimals);
  if (amt !== null && amt <= 0n) throw new Error('Enter a valid amount');
  const plans = {};
  let firstErr = null;
  for (const lvl of ['low', 'market', 'fast']) {
    try { plans[lvl] = plan(net, utxos, dest, own, amt, rates[lvl]); } catch (e) { firstErr = firstErr || e; }
  }
  return { net, plans, rates, err: firstErr, own };
}

export function summarize(p, rate) {
  return { fee: p.fee.toString(), amount: p.amount.toString(), change: p.change.toString(), inputs: p.inputs.length, rate: Math.round(rate * 10) / 10 };
}

export function signPlan(netId, mnemonic, index, p, ownScript) {
  const net = NETS[netId];
  const key = deriveKey(rootOf(mnemonic), net, index);
  return signTx(net, p, key, ownScript);
}

export const _test = { cashEncode, cashDecode, b58check, b58checkDecode, bech32Encode, bech32Decode, hash160, sha256d, deriveKey, rootOf, addressOf, plan, signTx, scriptFor, cat };
