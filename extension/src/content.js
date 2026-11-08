// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
// Bridge between the web page and the extension. The page never talks to the extension directly
const api = globalThis.browser || globalThis.chrome;
const post = (m) => window.postMessage({ target: 'sillet-inpage', ...m }, '*');

async function ask(method, params) {
  try {
    const r = await api.runtime.sendMessage({ type: 'rpc', method, params });
    return r || { ok: false, error: { code: -32603, message: 'No answer from Sillet' } };
  } catch (e) {
    return { ok: false, error: { code: -32603, message: 'Sillet was updated or restarted. Reload the page' } };
  }
}

window.addEventListener('message', async (ev) => {
  if (ev.source !== window) return;
  const d = ev.data;
  if (!d || d.target !== 'sillet-content') return;
  if (d.type === 'hello') { post({ type: 'ready' }); return; }
  if (d.type !== 'request' || typeof d.method !== 'string') return;
  const res = await ask(d.method, d.params);
  post({ type: 'response', id: d.id, ok: res.ok, result: res.result, error: res.error });
});

api.runtime.onMessage.addListener((msg) => {
  if (msg && msg.type === 'sillet-state') refresh();
});

async function refresh() {
  const [a, c] = await Promise.all([ask('eth_accounts', []), ask('eth_chainId', [])]);
  if (a.ok && c.ok) post({ type: 'state', accounts: a.result, chainId: c.result });
}

post({ type: 'ready' });
