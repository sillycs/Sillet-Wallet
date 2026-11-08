// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
import { chainById } from './chains.js';
import { RpcError } from './errors.js';

const preferred = {};
let counter = 1;
const FAILOVER_CODES = new Set([-32005, -32007, -32016, 429]);

async function post(url, body) {
  const ctl = new AbortController();
  const timer = setTimeout(() => ctl.abort(), 12000);
  try {
    const res = await fetch(url, {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify(body),
      signal: ctl.signal,
      credentials: 'omit',
      cache: 'no-store',
    });
    if (!res.ok) throw new RpcError(res.status === 429 ? 429 : -32603, 'RPC HTTP ' + res.status);
    return await res.json();
  } finally {
    clearTimeout(timer);
  }
}

// Calls the public nodes of a network, moving to the next one when a node is down or rate limited
export async function rpc(chainId, method, params = []) {
  const chain = chainById(chainId);
  if (!chain) throw new RpcError(4902, 'Unknown network');
  const urls = chain.rpc;
  const start = preferred[chainId] || 0;
  let last;
  for (let k = 0; k < urls.length; k++) {
    const i = (start + k) % urls.length;
    try {
      const out = await post(urls[i], { jsonrpc: '2.0', id: counter++, method, params });
      if (out && out.error) {
        const err = new RpcError(out.error.code, out.error.message || 'RPC error', out.error.data);
        if (FAILOVER_CODES.has(out.error.code)) { last = err; continue; }
        throw err;
      }
      preferred[chainId] = i;
      return out.result;
    } catch (e) {
      if (e instanceof RpcError && !FAILOVER_CODES.has(e.code) && e.code !== -32603 && e.code !== 429) throw e;
      last = e;
    }
  }
  throw new RpcError(-32603, 'Could not reach the network: ' + (last?.message || 'unknown error'));
}
