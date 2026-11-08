// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
// Round pictures for networks, tokens, accounts and websites. Drawn here, so no image is fetched from the internet
import { h } from './dom.js';
import have from '../../scripts/coins.build.json' with { type: 'json' };

const NS = 'http://www.w3.org/2000/svg';
export const svgEl = (tag, attrs, ...kids) => {
  const el = document.createElementNS(NS, tag);
  for (const [k, v] of Object.entries(attrs || {})) el.setAttribute(k, v);
  kids.forEach((k) => el.append(k));
  return el;
};

const DIAMOND = 'M12 3.2 6.2 12.4 12 15.8l5.8-3.4zM12 16.9l-5.8-3.4L12 20.8l5.8-7.3z';

const CHAIN_STYLE = {
  1: ['#627EEA', '#fff', 'diamond'], 42161: ['#28A0F0', '#fff', 'A'], 10: ['#FF0420', '#fff', 'OP'], 8453: ['#0052FF', '#fff', 'B'],
  137: ['#8247E5', '#fff', 'P'], 56: ['#F0B90B', '#1b1500', 'B'], 43114: ['#E84142', '#fff', 'A'], 8217: ['#BFF009', '#111', 'K'],
  59144: ['#61DFFF', '#06202a', 'L'], 81457: ['#FCFC03', '#111', 'B'], 100: ['#04795B', '#fff', 'G'],
  bitcoin: ['#F7931A', '#fff', '₿'], litecoin: ['#345D9D', '#fff', 'Ł'], dogecoin: ['#C2A633', '#fff', 'Ð'], bitcoincash: ['#8DC351', '#fff', '₿'],
};

const TOKEN_STYLE = {
  ETH: ['#627EEA', '#fff', 'diamond'], WETH: ['#627EEA', '#fff', 'diamond'], USDT: ['#26A17B', '#fff', '₮'], USDC: ['#2775CA', '#fff', '$'],
  DAI: ['#F5AC37', '#fff', '◈'], WBTC: ['#F7931A', '#fff', '₿'], BNB: ['#F0B90B', '#1b1500', 'B'], POL: ['#8247E5', '#fff', 'P'], MATIC: ['#8247E5', '#fff', 'P'],
  AVAX: ['#E84142', '#fff', 'A'], KAIA: ['#BFF009', '#111', 'K'], XDAI: ['#04795B', '#fff', 'G'], LINK: ['#2A5ADA', '#fff', 'L'], WLD: ['#e8e8ea', '#111', 'W'],
  FDUSD: ['#2d6cdf', '#fff', 'F'], UNI: ['#FF007A', '#fff', 'U'], ARB: ['#28A0F0', '#fff', 'A'], OP: ['#FF0420', '#fff', 'OP'], CBBTC: ['#F7931A', '#fff', '₿'], BTC: ['#F7931A', '#fff', '₿'], LTC: ['#345D9D', '#fff', 'Ł'], DOGE: ['#C2A633', '#fff', 'Ð'], BCH: ['#8DC351', '#fff', '₿'],
};

const PALETTE = ['#3B82F6', '#00A67E', '#E5484D', '#F5A524', '#0EA5E9', '#14B8A6', '#F97316', '#52525B', '#64748B', '#8C8C96'];

function hash(str) {
  let x = 2166136261;
  for (let i = 0; i < str.length; i++) { x ^= str.charCodeAt(i); x = Math.imul(x, 16777619); }
  return x >>> 0;
}

function disc(size, bg, fg, mark, cls) {
  const s = svgEl('svg', { viewBox: '0 0 40 40', width: size, height: size, class: 'avatar ' + (cls || '') });
  s.append(svgEl('circle', { cx: 20, cy: 20, r: 20, fill: bg }));
  if (mark === 'diamond') {
    s.append(svgEl('path', { d: DIAMOND, fill: fg, transform: 'translate(8 8) scale(1)' , 'fill-rule': 'evenodd' }));
  } else {
    const t = svgEl('text', { x: 20, y: 20.5, 'text-anchor': 'middle', 'dominant-baseline': 'central', 'font-family': 'system-ui,Segoe UI,Arial,sans-serif', 'font-weight': 700, 'font-size': mark.length > 1 ? 15 : 19, fill: fg });
    t.textContent = mark;
    s.append(t);
  }
  return s;
}

// The real picture is a file inside the extension. If a coin has none, a drawn disc is used
function picture(src, size, cls, fallback) {
  const img = h('img', { src, width: size, height: size, alt: '', class: 'avatar img ' + (cls || ''), draggable: 'false' });
  img.addEventListener('error', () => img.replaceWith(fallback()), { once: true });
  return img;
}

export function chainAvatar(chainId, size = 24) {
  const [bg, fg, mark] = CHAIN_STYLE[chainId] || ['#3A3A41', '#fff', '•'];
  const drawn = () => disc(size, bg, fg, mark, 'chain');
  return have.chains.map(String).includes(String(chainId)) ? picture('coins/chain-' + chainId + '.svg', size, 'chain', drawn) : drawn();
}

export function tokenAvatar(symbol, size = 40) {
  const key = String(symbol || '?').toUpperCase();
  const known = TOKEN_STYLE[key];
  const drawn = () => (known ? disc(size, known[0], known[1], known[2]) : disc(size, PALETTE[hash(key) % PALETTE.length], '#fff', key.slice(0, 1)));
  return have.tokens.includes(key) ? picture('coins/token-' + key + '.svg', size, '', drawn) : drawn();
}

// A coin picture with the small network badge in the corner
export function coinWithBadge(symbol, chainId, size = 40) {
  return h('span', { class: 'coin', style: `width:${size}px;height:${size}px` }, tokenAvatar(symbol, size), chainId ? h('span', { class: 'badge' }, chainAvatar(chainId, Math.round(size * 0.42))) : null);
}

// Unique picture per address: a soft gradient with a pattern, so two accounts are easy to tell apart
export function identicon(address, size = 36) {
  const n = hash(String(address || '0').toLowerCase());
  const a = PALETTE[n % PALETTE.length];
  const b = PALETTE[(n >>> 5) % PALETTE.length];
  const id = 'g' + n;
  const s = svgEl('svg', { viewBox: '0 0 40 40', width: size, height: size, class: 'avatar' });
  const defs = svgEl('defs', {}, svgEl('linearGradient', { id, x1: 0, y1: 0, x2: 1, y2: 1 }, svgEl('stop', { offset: 0, 'stop-color': a }), svgEl('stop', { offset: 1, 'stop-color': b })));
  s.append(defs, svgEl('circle', { cx: 20, cy: 20, r: 20, fill: `url(#${id})` }));
  for (let i = 0; i < 3; i++) {
    const v = hash(address + i);
    s.append(svgEl('circle', { cx: 8 + (v % 24), cy: 8 + ((v >>> 7) % 24), r: 3 + ((v >>> 13) % 5), fill: '#fff', 'fill-opacity': 0.22 }));
  }
  return s;
}

export function siteAvatar(origin, size = 36) {
  let host = origin;
  try { host = new URL(origin).hostname; } catch (e) { /* keep */ }
  const letter = host.replace(/^www\./, '').slice(0, 1).toUpperCase() || '?';
  const bg = PALETTE[hash(host) % PALETTE.length];
  return disc(size, bg, '#fff', letter);
}
