// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
// Optional USD prices from CoinGecko. Off by default; the person turns it on in Sillet.
// Only token addresses of the selected network and the coin names are sent, never your address
const IDS = {
  1: { coin: 'ethereum', platform: 'ethereum' }, 42161: { coin: 'ethereum', platform: 'arbitrum-one' }, 10: { coin: 'ethereum', platform: 'optimistic-ethereum' },
  8453: { coin: 'ethereum', platform: 'base' }, 137: { coin: 'polygon-ecosystem-token', platform: 'polygon-pos' }, 56: { coin: 'binancecoin', platform: 'binance-smart-chain' },
  43114: { coin: 'avalanche-2', platform: 'avalanche' }, 8217: { coin: 'kaia', platform: 'kaia' }, 59144: { coin: 'ethereum', platform: 'linea' },
  81457: { coin: 'ethereum', platform: 'blast' }, 100: { coin: 'xdai', platform: 'xdai' },
  bitcoin: { coin: 'bitcoin' }, litecoin: { coin: 'litecoin' }, dogecoin: { coin: 'dogecoin' }, bitcoincash: { coin: 'bitcoin-cash' },
};
// eslint-disable-next-line no-undef
const TEST = typeof __SILLET_TEST_BASE__ === 'string' ? __SILLET_TEST_BASE__ : '';
const API = TEST ? TEST + '/cg' : 'https://api.coingecko.com/api/v3';
const cache = new Map();

async function getJson(url) {
  const ctl = new AbortController();
  const timer = setTimeout(() => ctl.abort(), 8000);
  try {
    const r = await fetch(url, { signal: ctl.signal, headers: { accept: 'application/json' }, credentials: 'omit', referrerPolicy: 'no-referrer' });
    if (!r.ok) throw new Error('Prices are not available right now');
    return await r.json();
  } finally { clearTimeout(timer); }
}

export async function pricesFor(chainId, tokenAddresses) {
  const ids = IDS[chainId];
  if (!ids) return { coin: null, tokens: {} };
  const key = chainId + ':' + tokenAddresses.map((a) => a.toLowerCase()).sort().join(',');
  const hit = cache.get(key);
  if (hit && Date.now() - hit.at < 60000) return hit.value;
  const jobs = [getJson(`${API}/simple/price?ids=${ids.coin}&vs_currencies=usd&include_24hr_change=true`).catch(() => null)];
  if (tokenAddresses.length) jobs.push(getJson(`${API}/simple/token_price/${ids.platform}?contract_addresses=${tokenAddresses.slice(0, 40).join(',')}&vs_currencies=usd`).catch(() => null));
  const [coin, tok] = await Promise.all(jobs);
  const value = { coin: coin && coin[ids.coin] ? { usd: coin[ids.coin].usd, change: coin[ids.coin].usd_24h_change } : null, tokens: {} };
  for (const [addr, v] of Object.entries(tok || {})) if (v && typeof v.usd === 'number') value.tokens[addr.toLowerCase()] = v.usd;
  cache.set(key, { at: Date.now(), value });
  return value;
}

// The Market tab: the biggest coins with 24 hour and 7 day change and a small chart. Only coin names are asked for, nothing about you
let marketCache = null;
const num = (v) => (typeof v === 'number' && Number.isFinite(v) ? v : null);
export async function marketList() {
  if (marketCache && Date.now() - marketCache.at < 90000) return marketCache.value;
  const rows = await getJson(`${API}/coins/markets?vs_currency=usd&order=market_cap_desc&per_page=100&page=1&sparkline=true&price_change_percentage=24h%2C7d`);
  if (!Array.isArray(rows)) throw new Error('Prices are not available right now');
  const value = rows.map((r) => {
    const line = Array.isArray(r.sparkline_in_7d && r.sparkline_in_7d.price) ? r.sparkline_in_7d.price.filter((x) => typeof x === 'number') : [];
    const step = Math.max(1, Math.floor(line.length / 42));
    return {
      id: String(r.id), symbol: String(r.symbol || '').toUpperCase(), name: String(r.name || ''), rank: num(r.market_cap_rank), price: num(r.current_price),
      cap: num(r.market_cap), volume: num(r.total_volume), high: num(r.high_24h), low: num(r.low_24h), ath: num(r.ath),
      c24: num(r.price_change_percentage_24h), c7: num(r.price_change_percentage_7d_in_currency), line: line.filter((_, i) => i % step === 0),
    };
  }).filter((r) => r.price != null);
  marketCache = { at: Date.now(), value };
  return value;
}
