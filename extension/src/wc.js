// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
// WalletConnect: lets dapps that show a QR code or a wc: link connect to Sillet, and routes their requests
// through the same approval windows as the browser provider. Needs a WalletConnect project id (see walletconnect.json)
import { Core } from '@walletconnect/core';
import { WalletKit } from '@reown/walletkit';
import { buildApprovedNamespaces, getSdkError } from '@walletconnect/utils';
import { getAddress } from 'ethers';
import { api } from './api.js';
import * as store from './state.js';
import * as core from './core.js';
import * as approvals from './approvals.js';
import { chainById, toHex } from './chains.js';
import { handleRpc } from './handler.js';
import { RpcError } from './errors.js';

// eslint-disable-next-line no-undef
const PROJECT = typeof __SILLET_WC_PROJECT__ === 'string' ? __SILLET_WC_PROJECT__ : '';
// eslint-disable-next-line no-undef
const RELAY = typeof __SILLET_WC_RELAY__ === 'string' && __SILLET_WC_RELAY__ ? __SILLET_WC_RELAY__ : undefined;
export const configured = () => /^[0-9a-f]{32}$/i.test(PROJECT);

const METHODS = ['personal_sign', 'eth_signTypedData_v3', 'eth_signTypedData_v4', 'eth_sendTransaction', 'wallet_switchEthereumChain', 'wallet_addEthereumChain', 'wallet_watchAsset'];
const EVENTS = ['chainChanged', 'accountsChanged'];
const ALARM = 'sillet-wc';
const keyOf = (topic) => 'wc:' + topic;
export const isWcOrigin = (o) => typeof o === 'string' && o.startsWith('wc:');

let kitPromise = null;

async function create() {
  const wcCore = new Core({ projectId: PROJECT, relayUrl: RELAY, telemetryEnabled: false });
  const kit = await WalletKit.init({
    core: wcCore,
    metadata: { name: 'Sillet', description: 'Open source wallet by the Raton team', url: 'https://raton.fun/wallet', icons: ['https://raton.fun/wallet/icon.png'] },
  });
  kit.on('session_proposal', (p) => onProposal(kit, p).catch((e) => console.error('wc proposal', e && e.message)));
  kit.on('session_request', (r) => onRequest(kit, r).catch((e) => console.error('wc request', e && e.message)));
  kit.on('session_delete', (d) => forget(d.topic).catch(() => {}));
  kit.on('session_authenticate', (a) => { try { kit.rejectSessionAuthenticate({ id: a.id, reason: getSdkError('UNSUPPORTED_METHODS') }).catch(() => {}); } catch (e) { /* ignore */ } });
  return kit;
}

export function getKit() {
  if (!configured()) return Promise.reject(new Error('WalletConnect is not set up in this build yet'));
  if (!kitPromise) kitPromise = create().catch((e) => { kitPromise = null; throw e; });
  return kitPromise;
}

async function wake() {
  const st = await store.load();
  const has = Object.keys(st.permissions).some(isWcOrigin);
  if (has) { try { api.alarms.create(ALARM, { periodInMinutes: 0.5 }); } catch (e) { /* ignore */ } } else { try { api.alarms.clear(ALARM); } catch (e) { /* ignore */ } }
  return has;
}

// Called whenever the background wakes up. If there are WalletConnect sessions, reconnect so requests can arrive
export async function resume() {
  if (!configured()) return;
  if (await wake()) await getKit();
}
export const onAlarm = (name) => { if (name === ALARM) resume().catch(() => {}); };
export const ALARM_NAME = ALARM;

// ---------- connecting ----------

const cleanName = (s, n) => String(s || '').replace(/[\u0000-\u001f\u007f<>]/g, '').slice(0, n);

export async function pair(uri) {
  const u = String(uri || '').trim();
  if (!/^wc:[0-9a-f]{64}@2\?/i.test(u) || u.length > 1500) throw new Error('That is not a WalletConnect link');
  const kit = await getKit();
  await kit.pair({ uri: u });
  return true;
}

function chainsFrom(ns) {
  const out = [];
  for (const c of (ns && ns.chains) || []) { const m = /^eip155:(\d+)$/.exec(c); if (m && chainById(Number(m[1]))) out.push(Number(m[1])); }
  return out;
}

async function onProposal(kit, proposal) {
  const { id, params } = proposal;
  const reject = (key) => kit.rejectSession({ id, reason: getSdkError(key) });
  const st = await store.load();
  if (!st.vault) return reject('USER_REJECTED');
  const req = (params.requiredNamespaces || {});
  const opt = (params.optionalNamespaces || {});
  if (Object.keys(req).some((k) => k !== 'eip155')) return reject('UNSUPPORTED_NAMESPACE_KEY');
  const reqChains = (req.eip155 && req.eip155.chains) || [];
  const wanted = chainsFrom(req.eip155);
  if (reqChains.length !== wanted.length) return reject('UNSUPPORTED_CHAINS');
  const chains = [...new Set([...wanted, ...chainsFrom(opt.eip155)])];
  if (!chains.length) return reject('UNSUPPORTED_CHAINS');
  const reqMethods = (req.eip155 && req.eip155.methods) || [];
  if (reqMethods.some((m) => !METHODS.includes(m))) return reject('UNSUPPORTED_METHODS');

  const meta = params.proposer.metadata || {};
  const vc = (proposal.verifyContext && proposal.verifyContext.verified) || {};
  const label = cleanName(meta.name, 40) || 'Unknown app';
  let host = '';
  try { host = new URL(meta.url).host; } catch (e) { host = ''; }
  let decision;
  try {
    decision = await approvals.request('connect', 'wc:proposal-' + id, {
      wc: true, label: label + (host ? ' · ' + host : ''), verified: vc.validation || 'UNKNOWN', verifiedOrigin: vc.origin || '',
      chains: chains.map((c) => chainById(c).name),
    });
  } catch (e) { return reject('USER_REJECTED'); }

  const account = getAddress(decision.account);
  const namespaces = buildApprovedNamespaces({
    proposal: params,
    supportedNamespaces: { eip155: { chains: chains.map((c) => 'eip155:' + c), methods: METHODS, events: EVENTS, accounts: chains.map((c) => `eip155:${c}:${account}`) } },
  });
  const session = await kit.approveSession({ id, namespaces });
  const key = keyOf(session.topic);
  await store.withLock(async () => {
    const cur = await store.load();
    await store.save({
      permissions: { ...cur.permissions, [key]: { accounts: [account], connectedAt: Date.now(), wc: { name: label, url: cleanName(meta.url, 200), verified: vc.validation || 'UNKNOWN' } } },
      siteChains: { ...cur.siteChains, [key]: chains[0] },
    });
  });
  await wake();
  core.stateChanged();
}

async function forget(topic) {
  const key = keyOf(topic);
  await store.withLock(async () => {
    const cur = await store.load();
    const permissions = { ...cur.permissions };
    const siteChains = { ...cur.siteChains };
    delete permissions[key]; delete siteChains[key];
    await store.save({ permissions, siteChains });
  });
  await wake();
  core.stateChanged();
}

export async function disconnect(origin) {
  if (!isWcOrigin(origin)) return;
  const topic = origin.slice(3);
  try { const kit = await getKit(); await kit.disconnectSession({ topic, reason: getSdkError('USER_DISCONNECTED') }); } catch (e) { /* the session may already be gone */ }
  await forget(topic);
}

// ---------- requests ----------

// If Sillet is locked, open the unlock screen and wait for a while instead of failing right away
async function waitUnlocked() {
  if (await core.isUnlocked()) return true;
  try { await api.windows.create({ url: api.runtime.getURL('popup.html#/'), type: 'popup', width: 400, height: 680, focused: true }); } catch (e) { /* no window */ }
  for (let i = 0; i < 90; i++) {
    await new Promise((r) => setTimeout(r, 1000));
    if (await core.isUnlocked()) return true;
  }
  return false;
}

async function onRequest(kit, event) {
  const { id, topic, params } = event;
  const respond = (body) => kit.respondSessionRequest({ topic, response: { id, jsonrpc: '2.0', ...body } });
  const fail = (code, message) => respond({ error: { code, message } });
  try {
    const key = keyOf(topic);
    const st = await store.load();
    const perm = st.permissions[key];
    if (!perm) return fail(4100, 'This connection is no longer active. Connect again');
    const { method, params: args } = params.request;
    const m = /^eip155:(\d+)$/.exec(params.chainId || '');
    const chainId = m ? Number(m[1]) : null;
    if (!METHODS.includes(method)) return fail(4200, 'Sillet does not support ' + String(method).slice(0, 60));
    const session = kit.getActiveSessions()[topic];
    const allowed = session && session.namespaces && session.namespaces.eip155 && session.namespaces.eip155.chains;
    if (!chainId || !chainById(chainId) || (allowed && !allowed.includes('eip155:' + chainId))) return fail(4901, 'This network was not approved for this connection');
    if (!(await waitUnlocked())) return fail(4100, 'Sillet is locked. Unlock it and try again');
    // Every request names its network, so the right one is used for this call
    if (st.siteChains[key] !== chainId) await store.withLock(async () => { const c = await store.load(); await store.save({ siteChains: { ...c.siteChains, [key]: chainId } }); });
    const result = await handleRpc({ origin: key, label: perm.wc && perm.wc.name }, method, args);
    await respond({ result: result === undefined ? null : result });
    if (method === 'wallet_switchEthereumChain' || method === 'wallet_addEthereumChain') {
      const now = (await store.load()).siteChains[key];
      kit.emitSessionEvent({ topic, event: { name: 'chainChanged', data: now }, chainId: 'eip155:' + now }).catch(() => {});
    }
  } catch (e) {
    const code = e instanceof RpcError || typeof e.code === 'number' ? e.code : 5000;
    await fail(code, String(e.message || 'Something went wrong').slice(0, 200));
  }
}

export async function sessionsInfo() {
  const st = await store.load();
  return Object.entries(st.permissions).filter(([k]) => isWcOrigin(k)).map(([origin, v]) => ({ origin, name: v.wc && v.wc.name, url: v.wc && v.wc.url, account: v.accounts[0], chainId: st.siteChains[origin] || st.chainId }));
}

export const _toHex = toHex;
