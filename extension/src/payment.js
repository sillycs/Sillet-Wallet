// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
// Reads crypto payment text: plain addresses and payment links (bitcoin:, litecoin:, dogecoin:, bitcoincash:, ethereum:)
// Nothing here moves money. The result only fills in the Send screen, where the person reviews everything
import { isAddress, getAddress } from 'ethers';
import { NETS, NET_IDS, validAddress, parseAmount } from './utxo.js';
import { chainById } from './chains.js';

const SCHEMES = { bitcoin: 'bitcoin', litecoin: 'litecoin', dogecoin: 'dogecoin', bitcoincash: 'bitcoincash' };

function query(str) {
  const out = {};
  for (const part of String(str || '').split('&')) {
    if (!part) continue;
    const i = part.indexOf('=');
    try { out[decodeURIComponent(i < 0 ? part : part.slice(0, i)).toLowerCase()] = i < 0 ? '' : decodeURIComponent(part.slice(i + 1).replace(/\+/g, ' ')); } catch (e) { /* skip bad pair */ }
  }
  return out;
}

// "2.014e18" and "1000000" both become an exact integer, or null
function bigFromSci(s) {
  const m = /^(\d+)(?:\.(\d+))?(?:e\+?(\d+))?$/i.exec(String(s || '').trim());
  if (!m) return null;
  const frac = m[2] || '';
  const exp = Number(m[3] || 0);
  if (exp < frac.length || exp > 80) return exp >= frac.length ? null : (frac.slice(exp).replace(/0/g, '') ? null : BigInt(m[1] + frac.slice(0, exp)));
  return BigInt(m[1] + frac + '0'.repeat(exp - frac.length));
}

const clean = (s, n) => String(s || '').replace(/[\u0000-\u001f\u007f<>]/g, '').slice(0, n);

// Returns { net, to, amount?, raw?, token?, label? } or null. net is a coin id (like "bitcoin") or an EVM chain id (a number), or "evm" when the link does not say
export function parsePayment(text) {
  const s = String(text || '').trim();
  if (!s || s.length > 600) return null;
  if (/^wc:[0-9a-f]{64}@2\?/i.test(s)) return { wc: true, uri: s };
  const m = /^([a-z]+):(.*)$/is.exec(s);
  if (m && SCHEMES[m[1].toLowerCase()]) {
    const net = SCHEMES[m[1].toLowerCase()];
    const q = m[2].indexOf('?');
    const target = (q < 0 ? m[2] : m[2].slice(0, q)).replace(/^\/\//, '');
    const params = query(q < 0 ? '' : m[2].slice(q + 1));
    // Bitcoin Cash addresses carry their own prefix
    const to = net === 'bitcoincash' && !/^bitcoincash:/i.test(target) ? 'bitcoincash:' + target : target;
    if (!validAddress(net, to)) return null;
    const out = { net, to };
    if (params.amount) { try { const a = parseAmount(params.amount, NETS[net].decimals); if (a > 0n) out.amount = params.amount; } catch (e) { /* ignore bad amount */ } }
    const label = clean(params.label || params.message, 60);
    if (label) out.label = label;
    return out;
  }
  if (m && m[1].toLowerCase() === 'ethereum') {
    // EIP-681: ethereum:[pay-]target[@chain][/function]?params
    const rest = m[2].replace(/^pay-/i, '').replace(/^\/\//, '');
    const q = rest.indexOf('?');
    const head = q < 0 ? rest : rest.slice(0, q);
    const params = query(q < 0 ? '' : rest.slice(q + 1));
    const hm = /^(0x[0-9a-fA-F]{40})(?:@(\d+))?(?:\/([a-zA-Z_]+))?$/.exec(head);
    if (!hm || !isAddress(hm[1])) return null;
    const chain = hm[2] ? Number(hm[2]) : 'evm';
    if (hm[2] && !chainById(chain)) return null;
    if (hm[3] === 'transfer') {
      if (!params.address || !isAddress(params.address)) return null;
      const out = { net: chain, to: getAddress(params.address), token: getAddress(hm[1]) };
      const raw = bigFromSci(params.uint256);
      if (raw) out.raw = raw.toString();
      return out;
    }
    if (hm[3]) return null;
    const out = { net: chain, to: getAddress(hm[1]) };
    const raw = bigFromSci(params.value);
    if (raw) out.raw = raw.toString();
    return out;
  }
  return detectAddress(s);
}

// A bare address. The same legacy "1…" address is valid on Bitcoin and Bitcoin Cash, so Bitcoin wins unless the text says bitcoincash:
export function detectAddress(text) {
  const s = String(text || '').trim();
  if (isAddress(s)) return { net: 'evm', to: getAddress(s) };
  const order = ['bitcoincash', 'bitcoin', 'litecoin', 'dogecoin'];
  if (/^bitcoincash:/i.test(s) || /^[qp][0-9a-z]{41}$/i.test(s)) { if (validAddress('bitcoincash', s)) return { net: 'bitcoincash', to: /^bitcoincash:/i.test(s) ? s.toLowerCase() : 'bitcoincash:' + s.toLowerCase() }; }
  for (const id of order.filter((x) => NET_IDS.includes(x))) if (id !== 'bitcoincash' && validAddress(id, s)) return { net: id, to: s };
  return null;
}

// Pulls every valid address out of a piece of page text
export function findAddresses(text, max = 12) {
  const found = new Map();
  const re = /(?:bitcoincash:)?[qp][0-9a-z]{41}\b|\b0x[0-9a-fA-F]{40}\b|\b(?:bc1|ltc1)[0-9a-z]{20,70}\b|\b[13LMD][1-9A-HJ-NP-Za-km-z]{25,34}\b/gi;
  for (const c of String(text || '').match(re) || []) {
    const hit = detectAddress(c);
    if (hit && !found.has(hit.to)) { found.set(hit.to, hit); if (found.size >= max) break; }
  }
  return [...found.values()];
}

// The hash that carries a payment to the Sillet window
export function payHash(p, origin) {
  if (p.wc) return '/wc?uri=' + encodeURIComponent(p.uri);
  const q = new URLSearchParams({ net: String(p.net), to: p.to });
  for (const k of ['amount', 'raw', 'token', 'label']) if (p[k]) q.set(k, p[k]);
  if (origin) q.set('from', origin);
  return '/pay?' + q.toString();
}
