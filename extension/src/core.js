// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
// Wallet logic shared by the website requests and the Sillet window
import { api } from './api.js';
import * as store from './state.js';
import { encryptVault, decryptVault } from './vault.js';
import { walletAt, validPhrase, normalizePhrase } from './wallet.js';
import { addressesFor, NET_IDS } from './utxo.js';

const ALARM = 'sillet-autolock';
const notifyListeners = new Set();
export const onStateChanged = (fn) => notifyListeners.add(fn);
export const stateChanged = () => notifyListeners.forEach((fn) => { try { fn(); } catch (e) { /* ignore */ } });

export async function isUnlocked() {
  return !!(await store.getSession());
}

export async function touch() {
  const st = await store.load();
  const min = Number(st.settings.autolockMin);
  await api.alarms.clear(ALARM);
  if (min > 0 && (await isUnlocked())) api.alarms.create(ALARM, { delayInMinutes: Math.max(1, min) });
}

export async function lock() {
  await store.clearSession();
  await api.alarms.clear(ALARM);
  stateChanged();
}

export async function unlock(password) {
  const st = await store.load();
  if (!st.vault) throw new Error('No wallet yet');
  const mnemonic = await decryptVault(st.vault, String(password || ''));
  await store.setSession({ mnemonic });
  await touch();
  stateChanged();
}

export async function setup(mnemonic, password) {
  if (!validPhrase(mnemonic)) throw new Error('That recovery phrase is not valid');
  if (String(password || '').length < 8) throw new Error('Use a password with at least 8 characters');
  const phrase = normalizePhrase(mnemonic);
  const vault = await encryptVault(phrase, password);
  const address = walletAt(phrase, 0).address;
  await store.save({ vault, accounts: [{ index: 0, address, name: 'Account 1', addrs: addressesFor(phrase, 0) }], selected: 0, permissions: {}, siteChains: {}, history: [] });
  await store.setSession({ mnemonic: phrase });
  await touch();
  stateChanged();
}

export async function getWallet(index) {
  const s = await store.getSession();
  if (!s) return null;
  return walletAt(s.mnemonic, index);
}

export async function walletForAddress(address) {
  const st = await store.load();
  const acc = st.accounts.find((a) => a.address.toLowerCase() === String(address).toLowerCase());
  if (!acc) return null;
  return getWallet(acc.index);
}

export async function addAccount() {
  return store.withLock(async () => {
    const st = await store.load();
    const s = await store.getSession();
    if (!s) throw new Error('Unlock Sillet first');
    const index = st.accounts.reduce((m, a) => Math.max(m, a.index), -1) + 1;
    const address = walletAt(s.mnemonic, index).address;
    const accounts = [...st.accounts, { index, address, name: 'Account ' + (st.accounts.length + 1), addrs: addressesFor(s.mnemonic, index) }];
    await store.save({ accounts, selected: accounts.length - 1 });
    return accounts;
  });
}

// Bitcoin, Litecoin and Dogecoin addresses are saved with each account (public information).
// Accounts made by an older version get theirs the first time Sillet is open
export async function ensureAddrs() {
  const s = await store.getSession();
  if (!s) return;
  const st0 = await store.load();
  const missing = (a) => !a.addrs || NET_IDS.some((id) => !a.addrs[id]);
  if (!st0.accounts.some(missing)) return;
  await store.withLock(async () => {
    const st = await store.load();
    const accounts = st.accounts.map((a) => (missing(a) ? { ...a, addrs: addressesFor(s.mnemonic, a.index) } : a));
    await store.save({ accounts });
  });
}

export async function revealPhrase(password) {
  const st = await store.load();
  return decryptVault(st.vault, String(password || ''));
}

export async function changePassword(oldPassword, newPassword) {
  if (String(newPassword || '').length < 8) throw new Error('Use a password with at least 8 characters');
  const st = await store.load();
  const mnemonic = await decryptVault(st.vault, String(oldPassword || ''));
  await store.save({ vault: await encryptVault(mnemonic, newPassword) });
}

export async function wipe() {
  await api.storage.local.clear();
  await store.clearSession();
  await api.alarms.clear(ALARM);
  stateChanged();
}

export const ALARM_NAME = ALARM;
