// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
import { api } from './api.js';
import { E } from './errors.js';
import * as store from './state.js';

// Requests that need the person to say yes. Each one opens a small Sillet window
const pending = new Map();
let seq = 0;
const SINGLE = new Set(['connect', 'switchChain', 'watchAsset']);

export function listForOrigin(origin) {
  return [...pending.values()].filter((p) => p.record.origin === origin);
}

export function get(id) {
  const p = pending.get(id);
  return p ? p.record : null;
}

export async function request(kind, origin, data) {
  const mine = listForOrigin(origin);
  if (SINGLE.has(kind) && mine.some((p) => p.record.kind === kind)) throw E.pending();
  if (mine.length >= 3) throw E.pending();
  const id = Date.now().toString(36) + '-' + (++seq);
  // WalletConnect connections have no web address, so show the name the app gave itself
  let label = data && data.label;
  if (!label && origin.startsWith('wc:')) { const st = await store.load(); const w = st.permissions[origin] && st.permissions[origin].wc; label = w ? w.name : undefined; }
  const record = { id, kind, origin, createdAt: Date.now(), ...data, label, viaWc: origin.startsWith('wc:') };
  return new Promise(async (resolve, reject) => {
    const entry = { record, resolve, reject, windowId: null };
    pending.set(id, entry);
    try {
      const win = await api.windows.create({ url: api.runtime.getURL('popup.html#/approve/' + id), type: 'popup', width: 400, height: 680, focused: true });
      entry.windowId = win && win.id;
    } catch (e) {
      pending.delete(id);
      reject(E.internal('Could not open the Sillet window'));
    }
  });
}

export function resolve(id, decision) {
  const p = pending.get(id);
  if (!p) return false;
  pending.delete(id);
  p.resolve(decision);
  if (p.windowId != null) api.windows.remove(p.windowId).catch(() => {});
  return true;
}

export function reject(id) {
  const p = pending.get(id);
  if (!p) return false;
  pending.delete(id);
  p.reject(E.userRejected());
  if (p.windowId != null) api.windows.remove(p.windowId).catch(() => {});
  return true;
}

export function onWindowClosed(windowId) {
  for (const [id, p] of pending) {
    if (p.windowId === windowId) {
      pending.delete(id);
      p.reject(E.userRejected());
    }
  }
}
