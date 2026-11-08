// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
import { api } from './api.js';

export const DEFAULTS = {
  vault: null,
  accounts: [],
  selected: 0,
  chainId: 1,
  siteChains: {},
  permissions: {},
  history: [],
  tokens: {},
  contacts: [],
  favorites: [],
  scannerOff: [],
  settings: { autolockMin: 15, lang: 'auto', prices: false, hideBalances: false, detectPages: true },
};

let chain = Promise.resolve();
// Serialises read-modify-write so two requests cannot overwrite each other
export function withLock(fn) {
  const run = chain.then(fn, fn);
  chain = run.catch(() => {});
  return run;
}

export async function load() {
  const s = await api.storage.local.get(null);
  return { ...DEFAULTS, ...s, settings: { ...DEFAULTS.settings, ...(s.settings || {}) } };
}

export const save = (patch) => api.storage.local.set(patch);

export async function getSession() {
  const r = await api.storage.session.get('s');
  return r.s || null;
}
export const setSession = (s) => api.storage.session.set({ s });
export const clearSession = () => api.storage.session.remove('s');

export function siteChain(st, origin) {
  return st.siteChains[origin] || st.chainId;
}

export function permitted(st, origin) {
  const p = st.permissions[origin];
  return p ? p.accounts : [];
}
